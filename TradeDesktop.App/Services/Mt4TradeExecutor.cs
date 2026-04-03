using TradeDesktop.App.Native;

namespace TradeDesktop.App.Services;

public sealed class Mt4TradeExecutor : ITradePlatformExecutor
{
    private const string Mt4NotImplementedMessage = "MT4 executor is not implemented yet";

    public TradeLegPlatform Platform => TradeLegPlatform.Mt4;

    public Task<ManualTradeResult> OpenPairAsync(TradeOpenPairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return Task.FromResult(new ManualTradeResult(
            Label: "OPEN_MANUAL",
            Success: false,
            Legs:
            [
                BuildNotSupportedLeg(request.LegA.Exchange, request.LegA.Action),
                BuildNotSupportedLeg(request.LegB.Exchange, request.LegB.Action)
            ],
            ErrorMessage: Mt4NotImplementedMessage));
    }

    public Task<ManualTradeResult> ClosePairAsync(TradeClosePairRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Keep MT4 native type referenced intentionally as skeleton marker for upcoming implementation.
        _ = typeof(NativeMethodsMt4);

        var legA = request.LegA is null
            ? new ManualTradeLegResult("A", "CLOSE", true, "Close A skipped: no open trade")
            : BuildNotSupportedLeg(request.LegA.Exchange, TradeLegAction.Close);

        var legB = request.LegB is null
            ? new ManualTradeLegResult("B", "CLOSE", true, "Close B skipped: no open trade")
            : BuildNotSupportedLeg(request.LegB.Exchange, TradeLegAction.Close);

        return Task.FromResult(new ManualTradeResult(
            Label: "CLOSE_MANUAL",
            Success: false,
            Legs: [legA, legB],
            ErrorMessage: Mt4NotImplementedMessage));
    }

    private static ManualTradeLegResult BuildNotSupportedLeg(string exchange, TradeLegAction action)
        => new(
            Exchange: exchange,
            Action: action.ToString().ToUpperInvariant(),
            Success: false,
            Detail: Mt4NotImplementedMessage);
}