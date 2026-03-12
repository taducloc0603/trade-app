using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;
using TradeDesktop.Domain.Models;
using DomainMarketData = TradeDesktop.Domain.Models.MarketData;

namespace TradeDesktop.Infrastructure.MarketData;

public sealed class MockSharedMemoryMarketDataReader : ISharedMemoryReader
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private decimal _lastMidPrice = 100.00m;
    private readonly Random _random = new();

    public event EventHandler<DomainMarketData>? MarketDataReceived;
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
            _worker = Task.Run(() => PublishLoopAsync(_cts.Token), CancellationToken.None);
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

        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // expected on stop
            }
        }
    }

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var marketData = NextMockTick();
            MarketDataReceived?.Invoke(this, marketData);

            var sanA = new ExchangeMetrics(
                Symbol: "BTCUSDT",
                Bid: marketData.Bid,
                Ask: marketData.Ask,
                Spread: marketData.Spread,
                LatencyMs: 5,
                Tps: 100,
                Time: marketData.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                MaxLatMs: 10,
                AvgLatMs: 7,
                IsConnected: true,
                Error: null);

            var sanB = sanA with
            {
                Bid = marketData.Bid - 0.01m,
                Ask = marketData.Ask + 0.01m,
                Spread = (marketData.Ask + 0.01m) - (marketData.Bid - 0.01m)
            };

            SnapshotReceived?.Invoke(this, new SharedMemorySnapshot(sanA, sanB, DateTime.UtcNow));
        }
    }

    private DomainMarketData NextMockTick()
    {
        var drift = (decimal)(_random.NextDouble() - 0.5) * 0.20m;
        _lastMidPrice = Math.Round(Math.Max(1m, _lastMidPrice + drift), 5);
        var spread = 0.01m + Math.Round((decimal)_random.NextDouble() * 0.04m, 5);
        var bid = Math.Round(_lastMidPrice - spread / 2m, 5);
        var ask = Math.Round(_lastMidPrice + spread / 2m, 5);

        return new DomainMarketData(
            Bid: bid,
            Ask: ask,
            Timestamp: DateTime.UtcNow,
            IsConnected: true);
    }
}