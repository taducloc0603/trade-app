using TradeDesktop.Application.Abstractions;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.Infrastructure.MarketData;

public sealed class MockSharedMemoryMarketDataReader : ISharedMemoryReader
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private decimal _lastMidPrice = 100.00m;
    private readonly Random _random = new();

    public event EventHandler<MarketData>? MarketDataReceived;

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
        }
    }

    private MarketData NextMockTick()
    {
        var drift = (decimal)(_random.NextDouble() - 0.5) * 0.20m;
        _lastMidPrice = Math.Round(Math.Max(1m, _lastMidPrice + drift), 5);
        var spread = 0.01m + Math.Round((decimal)_random.NextDouble() * 0.04m, 5);
        var bid = Math.Round(_lastMidPrice - spread / 2m, 5);
        var ask = Math.Round(_lastMidPrice + spread / 2m, 5);

        return new MarketData(
            Bid: bid,
            Ask: ask,
            Timestamp: DateTime.UtcNow,
            IsConnected: true);
    }
}