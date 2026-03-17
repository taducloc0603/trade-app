using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class GapSignalConfirmationEngine : IGapSignalConfirmationEngine
{
    private readonly SideWindowState _buyState = new();
    private readonly SideWindowState _sellState = new();
    private readonly List<GapSignalDebugEvent> _lastDebugEvents = [];

    public IReadOnlyList<GapSignalDebugEvent> LastDebugEvents => _lastDebugEvents;

    public IReadOnlyList<GapSignalTriggerResult> ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config)
    {
        _lastDebugEvents.Clear();

        var normalizedConfirm = Math.Abs(config.ConfirmGapPts);
        var normalizedOpen = Math.Abs(config.OpenPts);
        var normalizedHoldMs = Math.Max(0, config.HoldConfirmMs);

        var results = new List<GapSignalTriggerResult>(capacity: 2);

        var buyResult = ProcessSide(
            side: GapSignalSide.Buy,
            gap: snapshot.GapBuy,
            timestampUtc: snapshot.TimestampUtc,
            state: _buyState,
            debugEvents: _lastDebugEvents,
            holdConfirmMs: normalizedHoldMs,
            confirmGapPts: normalizedConfirm,
            openPts: normalizedOpen,
            isConfirmSatisfied: value => value >= normalizedConfirm,
            isOpenSatisfied: value => value >= normalizedOpen);
        if (buyResult is not null)
        {
            results.Add(buyResult);
        }

        var sellResult = ProcessSide(
            side: GapSignalSide.Sell,
            gap: snapshot.GapSell,
            timestampUtc: snapshot.TimestampUtc,
            state: _sellState,
            debugEvents: _lastDebugEvents,
            holdConfirmMs: normalizedHoldMs,
            confirmGapPts: normalizedConfirm,
            openPts: normalizedOpen,
            isConfirmSatisfied: value => value >= normalizedConfirm,
            isOpenSatisfied: value => value >= normalizedOpen);
        if (sellResult is not null)
        {
            results.Add(sellResult);
        }

        return results;
    }

    public void Reset()
    {
        _buyState.Reset();
        _sellState.Reset();
    }

    private static GapSignalTriggerResult? ProcessSide(
        GapSignalSide side,
        int? gap,
        DateTime timestampUtc,
        SideWindowState state,
        List<GapSignalDebugEvent> debugEvents,
        int holdConfirmMs,
        int confirmGapPts,
        int openPts,
        Func<int, bool> isConfirmSatisfied,
        Func<int, bool> isOpenSatisfied)
    {
        if (!gap.HasValue || !isConfirmSatisfied(gap.Value))
        {
            debugEvents.Add(new GapSignalDebugEvent(
                TimestampUtc: timestampUtc,
                Side: side,
                Stage: GapSignalDebugStage.ConfirmFailReset,
                Gap: gap,
                ElapsedMs: 0,
                ConfirmGapPts: confirmGapPts,
                OpenPts: openPts,
                HoldConfirmMs: holdConfirmMs,
                Reason: !gap.HasValue
                    ? "gap-missing-reset-window"
                    : "gap-below-confirm-reset-window"));

            state.Reset();
            return null;
        }

        if (!state.WindowStartUtc.HasValue)
        {
            state.WindowStartUtc = timestampUtc;
            state.Gaps.Clear();

            debugEvents.Add(new GapSignalDebugEvent(
                TimestampUtc: timestampUtc,
                Side: side,
                Stage: GapSignalDebugStage.HoldStart,
                Gap: gap,
                ElapsedMs: 0,
                ConfirmGapPts: confirmGapPts,
                OpenPts: openPts,
                HoldConfirmMs: holdConfirmMs,
                Reason: "confirm-pass-start-hold-window"));
        }

        state.LastTickUtc = timestampUtc;
        state.Gaps.Add(gap.Value);

        var elapsedMs = (timestampUtc - state.WindowStartUtc.Value).TotalMilliseconds;
        if (elapsedMs < holdConfirmMs)
        {
            debugEvents.Add(new GapSignalDebugEvent(
                TimestampUtc: timestampUtc,
                Side: side,
                Stage: GapSignalDebugStage.HoldProgress,
                Gap: gap,
                ElapsedMs: elapsedMs,
                ConfirmGapPts: confirmGapPts,
                OpenPts: openPts,
                HoldConfirmMs: holdConfirmMs,
                Reason: "confirm-pass-hold-in-progress"));

            return null;
        }

        if (state.Gaps.Count == 0 || state.Gaps.Any(v => !isConfirmSatisfied(v)))
        {
            debugEvents.Add(new GapSignalDebugEvent(
                TimestampUtc: timestampUtc,
                Side: side,
                Stage: GapSignalDebugStage.ConfirmFailReset,
                Gap: gap,
                ElapsedMs: elapsedMs,
                ConfirmGapPts: confirmGapPts,
                OpenPts: openPts,
                HoldConfirmMs: holdConfirmMs,
                Reason: "hold-window-contains-gap-below-confirm-reset-window"));

            state.Reset();
            return null;
        }

        var lastGap = state.Gaps[^1];
        if (!isOpenSatisfied(lastGap))
        {
            debugEvents.Add(new GapSignalDebugEvent(
                TimestampUtc: timestampUtc,
                Side: side,
                Stage: GapSignalDebugStage.HoldReachedOpenFailReset,
                Gap: lastGap,
                ElapsedMs: elapsedMs,
                ConfirmGapPts: confirmGapPts,
                OpenPts: openPts,
                HoldConfirmMs: holdConfirmMs,
                Reason: "hold-reached-but-last-gap-below-open-reset-window"));

            state.Reset();
            return null;
        }

        var result = new GapSignalTriggerResult(
            Triggered: true,
            Action: GapSignalAction.Open,
            Side: side,
            Gaps: state.Gaps.ToArray(),
            TriggeredAtUtc: timestampUtc);

        debugEvents.Add(new GapSignalDebugEvent(
            TimestampUtc: timestampUtc,
            Side: side,
            Stage: GapSignalDebugStage.OpenTriggered,
            Gap: lastGap,
            ElapsedMs: elapsedMs,
            ConfirmGapPts: confirmGapPts,
            OpenPts: openPts,
            HoldConfirmMs: holdConfirmMs,
            Reason: "hold-reached-and-last-gap-pass-open-trigger-open"));

        state.Reset();
        return result;
    }

    private sealed class SideWindowState
    {
        public DateTime? WindowStartUtc { get; set; }
        public DateTime? LastTickUtc { get; set; }
        public List<int> Gaps { get; } = [];

        public void Reset()
        {
            WindowStartUtc = null;
            LastTickUtc = null;
            Gaps.Clear();
        }
    }
}
