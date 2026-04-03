using System.Diagnostics;

namespace TradeDesktop.App.Services;

public sealed class TradeExecutionRouter : ITradeExecutionRouter
{
    private readonly ITradePlatformExecutor _mt4Executor;
    private readonly ITradePlatformExecutor _mt5Executor;

    public TradeExecutionRouter(IEnumerable<ITradePlatformExecutor> executors)
    {
        _mt4Executor = executors.First(x => x.Platform == TradeLegPlatform.Mt4);
        _mt5Executor = executors.First(x => x.Platform == TradeLegPlatform.Mt5);
    }

    public Task<ManualTradeResult> OpenPairAsync(TradeOpenPairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidatePlatformOrThrow(request.LegA.Platform, request.LegA.Exchange);
        ValidatePlatformOrThrow(request.LegB.Platform, request.LegB.Exchange);

        Debug.WriteLine($"[TradeRouter][Open] leg={request.LegA.Exchange}, platform={request.LegA.Platform}, action={request.LegA.Action}, chartHwnd={request.LegA.ChartHwnd}");
        Debug.WriteLine($"[TradeRouter][Open] leg={request.LegB.Exchange}, platform={request.LegB.Platform}, action={request.LegB.Action}, chartHwnd={request.LegB.ChartHwnd}");

        return ExecuteOpenPairPerLegAsync(request, cancellationToken);
    }

    public Task<ManualTradeResult> ClosePairAsync(TradeClosePairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var platformA = request.LegA?.Platform;
        var platformB = request.LegB?.Platform;

        if (platformA.HasValue)
        {
            ValidatePlatformOrThrow(platformA.Value, request.LegA!.Exchange);
            Debug.WriteLine($"[TradeRouter][Close] leg={request.LegA.Exchange}, platform={platformA.Value}, action={request.LegA.Action}, tradeHwnd={request.LegA.TradeHwnd}, ticket={request.LegA.Ticket}");
        }
        else
        {
            Debug.WriteLine("[TradeRouter][Close] leg=A, skipped (no close request)");
        }

        if (platformB.HasValue)
        {
            ValidatePlatformOrThrow(platformB.Value, request.LegB!.Exchange);
            Debug.WriteLine($"[TradeRouter][Close] leg={request.LegB.Exchange}, platform={platformB.Value}, action={request.LegB.Action}, tradeHwnd={request.LegB.TradeHwnd}, ticket={request.LegB.Ticket}");
        }
        else
        {
            Debug.WriteLine("[TradeRouter][Close] leg=B, skipped (no close request)");
        }

        return ExecuteClosePairPerLegAsync(request, cancellationToken);
    }

    private static void ValidatePlatformOrThrow(TradeLegPlatform platform, string exchange)
    {
        if (platform is TradeLegPlatform.Mt4 or TradeLegPlatform.Mt5)
        {
            return;
        }

        throw new InvalidOperationException($"Invalid platform for exchange {exchange}: {platform}");
    }

    private async Task<ManualTradeResult> ExecuteOpenPairPerLegAsync(TradeOpenPairRequest request, CancellationToken cancellationToken)
    {
        var executorA = ResolveExecutor(request.LegA.Platform);
        var executorB = ResolveExecutor(request.LegB.Platform);

        Debug.WriteLine($"[TradeRouter][Open] executorA={executorA.GetType().Name}, executorB={executorB.GetType().Name}");

        var legATask = executorA.OpenLegAsync(request.LegA, cancellationToken);
        var legBTask = executorB.OpenLegAsync(request.LegB, cancellationToken);
        var legs = await Task.WhenAll(legATask, legBTask);

        return new ManualTradeResult(
            Label: "OPEN_MANUAL",
            Success: legs.All(x => x.Success),
            Legs: legs);
    }

    private async Task<ManualTradeResult> ExecuteClosePairPerLegAsync(TradeClosePairRequest request, CancellationToken cancellationToken)
    {
        var tasks = new List<Task<ManualTradeLegResult>>(capacity: 2);

        if (request.LegA is not null)
        {
            var executorA = ResolveExecutor(request.LegA.Platform);
            Debug.WriteLine($"[TradeRouter][Close] executorA={executorA.GetType().Name}");
            tasks.Add(executorA.CloseLegAsync(request.LegA, cancellationToken));
        }

        if (request.LegB is not null)
        {
            var executorB = ResolveExecutor(request.LegB.Platform);
            Debug.WriteLine($"[TradeRouter][Close] executorB={executorB.GetType().Name}");
            tasks.Add(executorB.CloseLegAsync(request.LegB, cancellationToken));
        }

        if (tasks.Count == 0)
        {
            return new ManualTradeResult(
                Label: "CLOSE_MANUAL",
                Success: true,
                Legs: []);
        }

        var legs = await Task.WhenAll(tasks);
        return new ManualTradeResult(
            Label: "CLOSE_MANUAL",
            Success: legs.All(x => x.Success),
            Legs: legs);
    }

    private ITradePlatformExecutor ResolveExecutor(TradeLegPlatform platform)
    {
        return platform switch
        {
            TradeLegPlatform.Mt4 => _mt4Executor,
            TradeLegPlatform.Mt5 => _mt5Executor,
            _ => throw new InvalidOperationException($"Unsupported platform: {platform}")
        };
    }
}