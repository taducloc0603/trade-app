using System.Globalization;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class TradeSignalLogBuilder : ITradeSignalLogBuilder
{
    public IReadOnlyList<string> BuildLogLines(TradeSignalInstruction instruction)
    {
        var triggeredAtLocal = instruction.TriggeredAtUtc.ToLocalTime();
        return
        [
            BuildLegLine(triggeredAtLocal, instruction.ExchangeA),
            BuildLegLine(triggeredAtLocal, instruction.ExchangeB)
        ];
    }

    private static string BuildLegLine(DateTime triggeredAtLocal, TradeInstructionLeg leg)
    {
        var actionText = leg.Action == GapSignalAction.Open ? "OPEN" : "CLOSE";
        var sideText = leg.Side == GapSignalSide.Buy ? "BUY" : "SELL";
        var joinedGaps = string.Join("|", leg.Gaps);
        var lastGap = leg.LastGap ?? 0;
        var priceText = leg.Price.HasValue
            ? leg.Price.Value.ToString("0.#####", CultureInfo.InvariantCulture)
            : "-";

        return $"[{triggeredAtLocal:HH:mm:ss.fff}] {actionText} {sideText} {leg.Exchange} by GAP: {lastGap} at Price: {priceText} ({joinedGaps})";
    }
}
