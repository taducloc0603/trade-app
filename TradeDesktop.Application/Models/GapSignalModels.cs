namespace TradeDesktop.Application.Models;

public enum GapSignalAction
{
    Open = 0,
    Close = 1
}

public enum GapSignalSide
{
    Buy = 0,
    Sell = 1
}

public enum TradingFlowPhase
{
    WaitingOpen = 0,
    WaitingCloseFromGapBuy = 1,
    WaitingCloseFromGapSell = 2
}

public enum TradingOpenMode
{
    None = 0,
    GapBuy = 1,
    GapSell = 2
}

public enum TradingPositionSide
{
    None = 0,
    Buy = 1,
    Sell = 2
}

public sealed record GapSignalSnapshot(
    DateTime TimestampUtc,
    decimal? Bid,
    decimal? Ask,
    int? GapBuy,
    int? GapSell);

public sealed record GapSignalConfirmationConfig(
    int ConfirmGapPts,
    int OpenPts,
    int HoldConfirmMs,
    int CloseConfirmGapPts = 0,
    int ClosePts = 0,
    int CloseHoldConfirmMs = 0,
    int StartTimeHold = 0,
    int EndTimeHold = 0,
    int StartWaitTime = 0,
    int EndWaitTime = 0);

public sealed record GapSignalTriggerResult(
    bool Triggered,
    GapSignalAction Action,
    GapSignalTriggerType TriggerType,
    GapSignalSide PrimarySide,
    IReadOnlyList<int> BuyGaps,
    IReadOnlyList<int> SellGaps,
    int? LastBuyGap,
    int? LastSellGap,
    DateTime TriggeredAtUtc,
    decimal? LastBid,
    decimal? LastAsk);

public enum GapSignalTriggerType
{
    OpenByGapBuy = 0,
    OpenByGapSell = 1,
    CloseByGapBuy = 2,
    CloseByGapSell = 3
}

public sealed record TradeInstructionLeg(
    string Exchange,
    GapSignalAction Action,
    GapSignalSide Side,
    IReadOnlyList<int> Gaps,
    int? LastGap,
    decimal? Price);

public sealed record TradeSignalInstruction(
    DateTime TriggeredAtUtc,
    GapSignalTriggerType TriggerType,
    GapSignalAction Action,
    GapSignalSide PrimarySide,
    IReadOnlyList<int> TriggerGaps,
    int? LastTriggerGap,
    TradeInstructionLeg ExchangeA,
    TradeInstructionLeg ExchangeB);
