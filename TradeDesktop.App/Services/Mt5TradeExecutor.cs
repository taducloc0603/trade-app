namespace TradeDesktop.App.Services;

public sealed class Mt5TradeExecutor : ITradePlatformExecutor
{
    private readonly IMt5ManualTradeService _mt5ManualTradeService;

    public Mt5TradeExecutor(IMt5ManualTradeService mt5ManualTradeService)
    {
        _mt5ManualTradeService = mt5ManualTradeService;
    }

    public TradeLegPlatform Platform => TradeLegPlatform.Mt5;

    public Task<ManualTradeResult> OpenPairAsync(TradeOpenPairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.LegA.Action == TradeLegAction.Buy && request.LegB.Action == TradeLegAction.Sell)
        {
            return _mt5ManualTradeService.ExecuteBuyAsync(request.LegA.ChartHwnd, request.LegB.ChartHwnd, cancellationToken);
        }

        if (request.LegA.Action == TradeLegAction.Sell && request.LegB.Action == TradeLegAction.Buy)
        {
            return _mt5ManualTradeService.ExecuteSellAsync(request.LegA.ChartHwnd, request.LegB.ChartHwnd, cancellationToken);
        }

        return Task.FromResult(new ManualTradeResult(
            Label: "OPEN_MANUAL",
            Success: false,
            Legs:
            [
                new ManualTradeLegResult(request.LegA.Exchange, request.LegA.Action.ToString().ToUpperInvariant(), false, "Unsupported open pair action combination for MT5 executor"),
                new ManualTradeLegResult(request.LegB.Exchange, request.LegB.Action.ToString().ToUpperInvariant(), false, "Unsupported open pair action combination for MT5 executor")
            ],
            ErrorMessage: "Unsupported open pair action combination for MT5 executor"));
    }

    public Task<ManualTradeResult> ClosePairAsync(TradeClosePairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var closeA = request.LegA is null
            ? null
            : new ManualCloseRequest(request.LegA.Exchange, request.LegA.TradeHwnd, request.LegA.Ticket);

        var closeB = request.LegB is null
            ? null
            : new ManualCloseRequest(request.LegB.Exchange, request.LegB.TradeHwnd, request.LegB.Ticket);

        return _mt5ManualTradeService.ExecuteCloseAsync(closeA, closeB, cancellationToken);
    }
}