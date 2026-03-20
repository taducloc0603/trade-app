using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class TradeInstructionFactory : ITradeInstructionFactory
{
    public TradeSignalInstruction Create(GapSignalTriggerResult triggerResult)
    {
        var exchangeASide = triggerResult.PrimarySide;
        var exchangeBSide = OppositeSide(exchangeASide);

        var exchangeA = BuildLeg("A", triggerResult.Action, exchangeASide, triggerResult);
        var exchangeB = BuildLeg("B", triggerResult.Action, exchangeBSide, triggerResult);

        return new TradeSignalInstruction(
            TriggeredAtUtc: triggerResult.TriggeredAtUtc,
            Action: triggerResult.Action,
            PrimarySide: triggerResult.PrimarySide,
            ExchangeA: exchangeA,
            ExchangeB: exchangeB);
    }

    private static TradeInstructionLeg BuildLeg(
        string exchange,
        GapSignalAction action,
        GapSignalSide side,
        GapSignalTriggerResult triggerResult)
    {
        var gaps = side == GapSignalSide.Buy ? triggerResult.BuyGaps : triggerResult.SellGaps;
        var lastGap = side == GapSignalSide.Buy ? triggerResult.LastBuyGap : triggerResult.LastSellGap;
        var price = side == GapSignalSide.Buy ? triggerResult.LastBid : triggerResult.LastAsk;

        return new TradeInstructionLeg(
            Exchange: exchange,
            Action: action,
            Side: side,
            Gaps: gaps,
            LastGap: lastGap,
            Price: price);
    }

    private static GapSignalSide OppositeSide(GapSignalSide side)
        => side == GapSignalSide.Buy ? GapSignalSide.Sell : GapSignalSide.Buy;
}
