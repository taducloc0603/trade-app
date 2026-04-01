namespace TradeDesktop.Application.Models;

public sealed record ConfigRecord(
    string Id,
    string SansJson,
    string? HostName,
    int Point,
    int OpenPts,
    int ConfirmGapPts,
    int HoldConfirmMs,
    int ClosePts,
    int CloseConfirmGapPts,
    int CloseHoldConfirmMs,
    int StartTimeHold,
    int EndTimeHold,
    int StartWaitTime,
    int EndWaitTime,
    int ConfirmLatencyMs,
    int MaxGap,
    int MaxSpread);