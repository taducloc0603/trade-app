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

        var executor = ResolveExecutor(request.LegA.Platform, request.LegB.Platform);
        Debug.WriteLine($"[TradeRouter][Open] executor={executor.GetType().Name}");
        return executor.OpenPairAsync(request, cancellationToken);
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

        var executor = ResolveExecutorForClose(platformA, platformB);
        Debug.WriteLine($"[TradeRouter][Close] executor={executor.GetType().Name}");
        return executor.ClosePairAsync(request, cancellationToken);
    }

    private static void ValidatePlatformOrThrow(TradeLegPlatform platform, string exchange)
    {
        if (platform is TradeLegPlatform.Mt4 or TradeLegPlatform.Mt5)
        {
            return;
        }

        throw new InvalidOperationException($"Invalid platform for exchange {exchange}: {platform}");
    }

    private ITradePlatformExecutor ResolveExecutorForClose(TradeLegPlatform? legAPlatform, TradeLegPlatform? legBPlatform)
    {
        if (legAPlatform.HasValue && legBPlatform.HasValue)
        {
            return ResolveExecutor(legAPlatform.Value, legBPlatform.Value);
        }

        if (legAPlatform.HasValue)
        {
            return legAPlatform.Value == TradeLegPlatform.Mt4 ? _mt4Executor : _mt5Executor;
        }

        if (legBPlatform.HasValue)
        {
            return legBPlatform.Value == TradeLegPlatform.Mt4 ? _mt4Executor : _mt5Executor;
        }

        // Keep existing close-noop behavior when both legs are absent.
        return _mt5Executor;
    }

    private ITradePlatformExecutor ResolveExecutor(TradeLegPlatform legAPlatform, TradeLegPlatform legBPlatform)
    {
        if (legAPlatform == TradeLegPlatform.Mt5 && legBPlatform == TradeLegPlatform.Mt5)
        {
            return _mt5Executor;
        }

        if (legAPlatform == TradeLegPlatform.Mt4 || legBPlatform == TradeLegPlatform.Mt4)
        {
            return _mt4Executor;
        }

        throw new InvalidOperationException(
            $"Unsupported platform combination: legA={legAPlatform}, legB={legBPlatform}");
    }
}