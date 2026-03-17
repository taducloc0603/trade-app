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
