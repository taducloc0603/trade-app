using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.MarketData;

public sealed class SharedMemoryMarketDataReader : ISharedMemoryReader
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan TpsRollingWindow = TimeSpan.FromSeconds(1);
    private const double MaxReasonableLatencyMs = 60_000;
    private const int HeaderSize = 16;
    private const int QuoteRingOffset = 16;
    private const int QuoteRingSize = 64;
    private const int QuoteMessageSize = 48;
    private const int QuoteTimestampOffset = 8;
    private const int QuoteBidOffset = 16;
    private const int QuoteAskOffset = 24;
    private const int QuoteSymbolOffset = 32;
    private const int QuoteSymbolLength = 16;

    private readonly object _syncRoot = new();
    private readonly IRuntimeConfigProvider _runtimeConfigProvider;
    private readonly ConcurrentDictionary<string, Mt5RealtimeState> _mt5StatsByMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapReaderHandle> _mapReaders = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public SharedMemoryMarketDataReader(IRuntimeConfigProvider runtimeConfigProvider)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    public event EventHandler<SharedMemorySnapshot>? SnapshotReceived;

    public bool IsRunning { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);
            IsRunning = true;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? worker;

        lock (_syncRoot)
        {
            if (!IsRunning)
            {
                return;
            }

            _cts?.Cancel();
            worker = _worker;
            _worker = null;
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }

        if (worker is null)
        {
            return;
        }

        try
        {
            await worker.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            DisposeAllMapReaders();
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var mapName1 = _runtimeConfigProvider.CurrentMapName1;
            var mapName2 = _runtimeConfigProvider.CurrentMapName2;

            RefreshMapReaders(mapName1, mapName2);

            var sanA = ReadExchangeMetrics(mapName1, "SanA");
            var sanB = ReadExchangeMetrics(mapName2, "SanB");
            var timestamp = DateTime.UtcNow;

            var snapshot = new SharedMemorySnapshot(sanA, sanB, timestamp);
            SnapshotReceived?.Invoke(this, snapshot);

        }
    }

    private ExchangeMetrics ReadExchangeMetrics(string? mapName, string fallbackSymbol)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Disconnected(fallbackSymbol, "Map name rỗng");
        }

        var normalizedMapName = mapName.Trim();

        try
        {
            var handle = GetOrCreateMapReader(normalizedMapName);
            if (TryParseMt5QuoteRing(handle.Accessor, fallbackSymbol, out var fromMt5, out var quoteSeq, out var timeMsc))
            {
                return ApplyMt5RealtimeMetrics(normalizedMapName, fromMt5, quoteSeq, timeMsc) with { Error = null, IsConnected = true };
            }

            using var stream = handle.Mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

            var raw = ReadAllBytes(stream);
            if (raw.Length == 0)
            {
                _mt5StatsByMap.TryRemove(normalizedMapName, out _);
                return Disconnected(fallbackSymbol, "Shared memory rỗng");
            }

            if (TryParseJsonPayload(raw, out var fromJson))
            {
                _mt5StatsByMap.TryRemove(normalizedMapName, out _);
                return fromJson with { Error = null, IsConnected = true };
            }

            // TODO: Khi xác nhận chính xác struct MT5 từ shm-app/EA, thay parser heuristic này bằng struct mapping cố định.
            if (TryParseBinaryPayload(raw, fallbackSymbol, out var fromBinary))
            {
                _mt5StatsByMap.TryRemove(normalizedMapName, out _);
                return fromBinary with { Error = null, IsConnected = true };
            }

            _mt5StatsByMap.TryRemove(normalizedMapName, out _);
            return Disconnected(fallbackSymbol, "Không parse được payload shared memory");
        }
        catch (FileNotFoundException)
        {
            RemoveMapReader(normalizedMapName);
            _mt5StatsByMap.TryRemove(normalizedMapName, out _);
            return Disconnected(fallbackSymbol, $"Map không tồn tại: {mapName}");
        }
        catch (Exception ex)
        {
            RemoveMapReader(normalizedMapName);
            _mt5StatsByMap.TryRemove(normalizedMapName, out _);
            return Disconnected(fallbackSymbol, ex.Message);
        }
    }

    private MapReaderHandle GetOrCreateMapReader(string mapName)
    {
        lock (_syncRoot)
        {
            if (_mapReaders.TryGetValue(mapName, out var existing))
            {
                return existing;
            }

            var mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var created = new MapReaderHandle(mmf, accessor);
            _mapReaders[mapName] = created;
            return created;
        }
    }

    private void RefreshMapReaders(string? mapName1, string? mapName2)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(mapName1))
        {
            keep.Add(mapName1.Trim());
        }

        if (!string.IsNullOrWhiteSpace(mapName2))
        {
            keep.Add(mapName2.Trim());
        }

        lock (_syncRoot)
        {
            var stale = _mapReaders.Keys.Where(k => !keep.Contains(k)).ToList();
            foreach (var key in stale)
            {
                _mapReaders[key].Dispose();
                _mapReaders.Remove(key);
                _mt5StatsByMap.TryRemove(key, out _);
            }
        }
    }

    private void RemoveMapReader(string mapName)
    {
        lock (_syncRoot)
        {
            if (!_mapReaders.TryGetValue(mapName, out var handle))
            {
                return;
            }

            handle.Dispose();
            _mapReaders.Remove(mapName);
        }
    }

    private void DisposeAllMapReaders()
    {
        lock (_syncRoot)
        {
            foreach (var handle in _mapReaders.Values)
            {
                handle.Dispose();
            }

            _mapReaders.Clear();
        }
    }

    private static bool TryParseMt5QuoteRing(
        MemoryMappedViewAccessor accessor,
        string fallbackSymbol,
        out ExchangeMetrics metrics,
        out uint quoteSeq,
        out long timeMsc)
    {
        metrics = Disconnected(fallbackSymbol, "MT5 quote ring parse failed");
        quoteSeq = 0;
        timeMsc = 0;

        var capacity = accessor.Capacity;
        var required = QuoteRingOffset + (QuoteRingSize * QuoteMessageSize);
        if (capacity < required || capacity < HeaderSize)
        {
            return false;
        }

        quoteSeq = accessor.ReadUInt32(0);
        if (quoteSeq == 0)
        {
            return false;
        }

        // Theo cấu trúc hiện tại của MT5/PowerShell monitor: slot = quote_seq % QUOTE_RING_SIZE
        var slot = (int)(quoteSeq % QuoteRingSize);
        var baseOffset = QuoteRingOffset + (slot * QuoteMessageSize);
        if (baseOffset + QuoteMessageSize > capacity)
        {
            return false;
        }

        timeMsc = accessor.ReadInt64(baseOffset + QuoteTimestampOffset);
        var bid = accessor.ReadDouble(baseOffset + QuoteBidOffset);
        var ask = accessor.ReadDouble(baseOffset + QuoteAskOffset);
        var symbol = ReadAsciiString(accessor, baseOffset + QuoteSymbolOffset, QuoteSymbolLength);

        if (double.IsNaN(bid) || double.IsInfinity(bid) ||
            double.IsNaN(ask) || double.IsInfinity(ask) ||
            bid <= 0 || ask <= 0)
        {
            return false;
        }

        var bidDecimal = (decimal)bid;
        var askDecimal = (decimal)ask;
        var symbolText = string.IsNullOrWhiteSpace(symbol) ? fallbackSymbol : symbol;

        string timeText;
        if (timeMsc > 946_684_800_000 && timeMsc < 4_102_444_800_000)
        {
            // time_msc từ MT5 thường là unix epoch millisecond
            timeText = DateTimeOffset.FromUnixTimeMilliseconds(timeMsc)
                .ToLocalTime()
                .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        else
        {
            timeText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        metrics = new ExchangeMetrics(
            Symbol: symbolText,
            Bid: bidDecimal,
            Ask: askDecimal,
            Spread: askDecimal - bidDecimal,
            LatencyMs: null,
            Tps: null,
            Time: timeText,
            MaxLatMs: null,
            AvgLatMs: null,
            IsConnected: true,
            Error: null);

        return true;
    }

    private ExchangeMetrics ApplyMt5RealtimeMetrics(string mapName, ExchangeMetrics source, uint quoteSeq, long timeMsc)
    {
        var state = _mt5StatsByMap.GetOrAdd(mapName, _ => new Mt5RealtimeState());
        var now = DateTimeOffset.UtcNow;
        var nowMs = now.ToUnixTimeMilliseconds();

        lock (state.Sync)
        {
            float tps = 0;

            if (state.PreviousQuoteSeq.HasValue)
            {
                var seqDelta = CalculateQuoteSeqDelta(quoteSeq, state.PreviousQuoteSeq.Value);
                state.CumulativeTicks += seqDelta;
            }

            state.PreviousQuoteSeq = quoteSeq;
            state.TickSamples.Add(new TickSample(now, state.CumulativeTicks));

            var minSampleTime = now - TpsRollingWindow;
            while (state.TickSamples.Count > 1 && state.TickSamples[0].TimestampUtc < minSampleTime)
            {
                state.TickSamples.RemoveAt(0);
            }

            if (state.TickSamples.Count >= 2)
            {
                var first = state.TickSamples[0];
                var last = state.TickSamples[^1];
                var dtMs = (last.TimestampUtc - first.TimestampUtc).TotalMilliseconds;
                var tickDelta = last.CumulativeTicks - first.CumulativeTicks;
                if (dtMs > 0 && tickDelta >= 0)
                {
                    tps = (float)(tickDelta * 1000d / dtMs);
                }
            }

            var parsedLatency = TryParseLatencyFromTimeMsc(timeMsc, nowMs, PollInterval.TotalMilliseconds);
            var effectiveLatency = parsedLatency.HasValue && parsedLatency.Value <= (decimal)MaxReasonableLatencyMs
                ? parsedLatency.Value
                : (decimal)PollInterval.TotalMilliseconds;

            state.TotalLatencyMs += effectiveLatency;
            state.LatencySamples += 1;
            state.MaxLatencyMs = Math.Max(state.MaxLatencyMs, effectiveLatency);
            var avgLatency = state.LatencySamples > 0 ? state.TotalLatencyMs / state.LatencySamples : 0m;

            return source with
            {
                LatencyMs = effectiveLatency,
                Tps = tps,
                MaxLatMs = state.MaxLatencyMs,
                AvgLatMs = avgLatency
            };
        }
    }

    private static long CalculateQuoteSeqDelta(uint current, uint previous)
        => current >= previous
            ? current - previous
            : ((long)uint.MaxValue + 1 - previous) + current;

    private static decimal? TryParseLatencyFromTimeMsc(long timeMsc, long nowMs, double expectedMs)
    {
        if (timeMsc <= 0)
        {
            return null;
        }

        double? normalizedTimestampMs = null;
        if (timeMsc >= 1_000_000_000_000_000_000)
        {
            normalizedTimestampMs = timeMsc / 1_000_000d; // ns epoch
        }
        else if (timeMsc >= 1_000_000_000_000_000)
        {
            normalizedTimestampMs = timeMsc / 1_000d; // us epoch
        }
        else if (timeMsc >= 1_000_000_000_000)
        {
            normalizedTimestampMs = timeMsc; // ms epoch
        }
        else if (timeMsc >= 1_000_000_000 && timeMsc < 10_000_000_000)
        {
            normalizedTimestampMs = timeMsc * 1000d; // s epoch
        }

        if (normalizedTimestampMs.HasValue)
        {
            var diff = nowMs - normalizedTimestampMs.Value;
            return diff >= 0 ? (decimal)diff : 0m;
        }

        var dayMs = 24 * 60 * 60 * 1000L;
        double? intradayMs = null;
        if (timeMsc < dayMs)
        {
            intradayMs = timeMsc;
        }
        else if (timeMsc < dayMs * 1000L)
        {
            intradayMs = timeMsc / 1000d;
        }
        else if (timeMsc < dayMs * 1_000_000L)
        {
            intradayMs = timeMsc / 1_000_000d;
        }

        if (!intradayMs.HasValue)
        {
            return null;
        }

        static double CalcDayLatency(double nowDayMs, double tsDayMs)
        {
            var diff = nowDayMs - tsDayMs;
            if (diff < 0)
            {
                diff += 24 * 60 * 60 * 1000d;
            }

            return diff;
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs);
        var local = now.ToLocalTime();

        var localDayMs =
            (local.Hour * 60 * 60 * 1000d) +
            (local.Minute * 60 * 1000d) +
            (local.Second * 1000d) +
            local.Millisecond;

        var utcDayMs =
            (now.Hour * 60 * 60 * 1000d) +
            (now.Minute * 60 * 1000d) +
            (now.Second * 1000d) +
            now.Millisecond;

        var candidateLocal = CalcDayLatency(localDayMs, intradayMs.Value);
        var candidateUtc = CalcDayLatency(utcDayMs, intradayMs.Value);

        var selected = Math.Abs(candidateLocal - expectedMs) < Math.Abs(candidateUtc - expectedMs)
            ? candidateLocal
            : candidateUtc;

        return selected >= 0 ? (decimal)selected : 0m;
    }

    private sealed class Mt5RealtimeState
    {
        public object Sync { get; } = new();
        public uint? PreviousQuoteSeq { get; set; }
        public long CumulativeTicks { get; set; }
        public List<TickSample> TickSamples { get; } = [];
        public decimal MaxLatencyMs { get; set; }
        public decimal TotalLatencyMs { get; set; }
        public int LatencySamples { get; set; }
    }

    private sealed class MapReaderHandle(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor) : IDisposable
    {
        public MemoryMappedFile Mmf { get; } = mmf;
        public MemoryMappedViewAccessor Accessor { get; } = accessor;

        public void Dispose()
        {
            Accessor.Dispose();
            Mmf.Dispose();
        }
    }

    private sealed record TickSample(DateTimeOffset TimestampUtc, long CumulativeTicks);

    private static string ReadAsciiString(MemoryMappedViewAccessor accessor, long offset, int length)
    {
        var bytes = new byte[length];
        accessor.ReadArray(offset, bytes, 0, length);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool TryParseJsonPayload(byte[] raw, out ExchangeMetrics metrics)
    {
        metrics = Disconnected("-", "JSON parse failed");

        var payload = DecodeUtf8Payload(raw);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var obj = doc.RootElement;

            var symbol = GetString(obj, "symbol", "sym", "instrument") ?? "-";
            var bid = GetDecimal(obj, "bid", "bid_price", "best_bid");
            var ask = GetDecimal(obj, "ask", "ask_price", "best_ask");
            var spread = GetDecimal(obj, "spread", "sp") ?? CalculateSpread(bid, ask);
            var latencyMs = GetDecimal(obj, "latencyMs", "latency_ms", "latency");
            var tps = GetFloat(obj, "tps", "ticks_per_second");
            var time = GetString(obj, "time", "timestamp", "ts");
            var maxLatMs = GetDecimal(obj, "maxLatMs", "max_latency_ms", "max_latency");
            var avgLatMs = GetDecimal(obj, "avgLatMs", "avg_latency_ms", "avg_latency");

            metrics = new ExchangeMetrics(
                Symbol: symbol,
                Bid: bid,
                Ask: ask,
                Spread: spread,
                LatencyMs: latencyMs,
                Tps: tps,
                Time: time,
                MaxLatMs: maxLatMs,
                AvgLatMs: avgLatMs,
                IsConnected: true,
                Error: null);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseBinaryPayload(byte[] raw, string fallbackSymbol, out ExchangeMetrics metrics)
    {
        metrics = Disconnected(fallbackSymbol, "Binary parse failed");
        if (raw.Length < 16)
        {
            return false;
        }

        decimal? bid = TryGetNumber(raw, 0);
        decimal? ask = TryGetNumber(raw, 8);
        decimal? spread = TryGetNumber(raw, 16) ?? CalculateSpread(bid, ask);
        decimal? latency = TryGetNumber(raw, 24);
        float? tps = TryGetFloat(raw, 32);

        if (!bid.HasValue && !ask.HasValue)
        {
            return false;
        }

        metrics = new ExchangeMetrics(
            Symbol: fallbackSymbol,
            Bid: bid,
            Ask: ask,
            Spread: spread,
            LatencyMs: latency,
            Tps: tps,
            Time: DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            MaxLatMs: null,
            AvgLatMs: null,
            IsConnected: true,
            Error: null);

        return true;
    }

    private static decimal? TryGetNumber(byte[] raw, int offset)
    {
        if (offset + sizeof(double) <= raw.Length)
        {
            var d = BitConverter.ToDouble(raw, offset);
            if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 1_000_000_000)
            {
                return (decimal)d;
            }
        }

        if (offset + sizeof(float) <= raw.Length)
        {
            var f = BitConverter.ToSingle(raw, offset);
            if (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1_000_000_000)
            {
                return (decimal)f;
            }
        }

        return null;
    }

    private static float? TryGetFloat(byte[] raw, int offset)
    {
        if (offset + sizeof(double) <= raw.Length)
        {
            var d = BitConverter.ToDouble(raw, offset);
            if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 1_000_000_000)
            {
                return (float)d;
            }
        }

        if (offset + sizeof(float) <= raw.Length)
        {
            var f = BitConverter.ToSingle(raw, offset);
            if (!float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1_000_000_000)
            {
                return f;
            }
        }

        return null;
    }

    private static string DecodeUtf8Payload(byte[] raw)
    {
        var zeroIndex = Array.IndexOf(raw, (byte)0);
        var length = zeroIndex >= 0 ? zeroIndex : raw.Length;
        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(raw, 0, length).Trim();
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            if (element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return element.ToString();
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var d))
            {
                return d;
            }

            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static float? GetFloat(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var f))
            {
                return f;
            }

            if (element.ValueKind == JsonValueKind.String &&
                float.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal? CalculateSpread(decimal? bid, decimal? ask)
        => bid.HasValue && ask.HasValue ? ask.Value - bid.Value : null;

    private static ExchangeMetrics Disconnected(string symbol, string error)
        => new(
            Symbol: symbol,
            Bid: null,
            Ask: null,
            Spread: null,
            LatencyMs: null,
            Tps: null,
            Time: DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            MaxLatMs: null,
            AvgLatMs: null,
            IsConnected: false,
            Error: error);
}
