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
    WaitingClose = 1
}

public enum TradingPositionSide
{
    None = 0,
    Buy = 1,
    Sell = 2
}

public sealed record GapSignalSnapshot(
    DateTime TimestampUtc,
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
    GapSignalSide Side,
    IReadOnlyList<int> Gaps,
    DateTime TriggeredAtUtc);
