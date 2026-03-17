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

public sealed record GapSignalSnapshot(
    DateTime TimestampUtc,
    int? GapBuy,
    int? GapSell);

public sealed record GapSignalConfirmationConfig(
    int ConfirmGapPts,
    int OpenPts,
    int HoldConfirmMs);

public sealed record GapSignalTriggerResult(
    bool Triggered,
    GapSignalAction Action,
    GapSignalSide Side,
    IReadOnlyList<int> Gaps,
    DateTime TriggeredAtUtc);

public enum GapSignalDebugStage
{
    HoldStart = 0,
    HoldProgress = 1,
    ConfirmFailReset = 2,
    HoldReachedOpenFailReset = 3,
    OpenTriggered = 4
}

public sealed record GapSignalDebugEvent(
    DateTime TimestampUtc,
    GapSignalSide Side,
    GapSignalDebugStage Stage,
    int? Gap,
    double ElapsedMs,
    int ConfirmGapPts,
    int OpenPts,
    int HoldConfirmMs,
    string Reason);
