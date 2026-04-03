namespace TradeDesktop.App.Services;

public interface ITradePlatformExecutor
{
    TradeLegPlatform Platform { get; }

    Task<ManualTradeResult> OpenPairAsync(TradeOpenPairRequest request, CancellationToken cancellationToken = default);

    Task<ManualTradeResult> ClosePairAsync(TradeClosePairRequest request, CancellationToken cancellationToken = default);
}