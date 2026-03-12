using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.MarketData;

public sealed class SharedMemoryMarketDataReader : ISharedMemoryReader
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    private readonly object _syncRoot = new();
    private readonly IRuntimeConfigProvider _runtimeConfigProvider;
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
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var mapName1 = _runtimeConfigProvider.CurrentMapName1;
            var mapName2 = _runtimeConfigProvider.CurrentMapName2;

            var sanA = ReadExchangeMetrics(mapName1, "SanA");
            var sanB = ReadExchangeMetrics(mapName2, "SanB");
            var timestamp = DateTime.UtcNow;

            var snapshot = new SharedMemorySnapshot(sanA, sanB, timestamp);
            SnapshotReceived?.Invoke(this, snapshot);

        }
    }

    private static ExchangeMetrics ReadExchangeMetrics(string? mapName, string fallbackSymbol)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Disconnected(fallbackSymbol, "Map name rỗng");
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName.Trim(), MemoryMappedFileRights.Read);
            using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

            var raw = ReadAllBytes(stream);
            if (raw.Length == 0)
            {
                return Disconnected(fallbackSymbol, "Shared memory rỗng");
            }

            if (TryParseJsonPayload(raw, out var fromJson))
            {
                return fromJson with { Error = null, IsConnected = true };
            }

            // TODO: Khi xác nhận chính xác struct MT5 từ shm-app/EA, thay parser heuristic này bằng struct mapping cố định.
            if (TryParseBinaryPayload(raw, fallbackSymbol, out var fromBinary))
            {
                return fromBinary with { Error = null, IsConnected = true };
            }

            return Disconnected(fallbackSymbol, "Không parse được payload shared memory");
        }
        catch (FileNotFoundException)
        {
            return Disconnected(fallbackSymbol, $"Map không tồn tại: {mapName}");
        }
        catch (Exception ex)
        {
            return Disconnected(fallbackSymbol, ex.Message);
        }
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
            var tps = GetDecimal(obj, "tps", "ticks_per_second");
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
        decimal? tps = TryGetNumber(raw, 32);

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
