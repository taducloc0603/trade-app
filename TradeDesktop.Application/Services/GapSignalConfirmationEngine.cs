using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class GapSignalConfirmationEngine : IGapSignalConfirmationEngine, IOpenSignalEngine
{
    private readonly SideWindowState _buyState = new();
    private readonly SideWindowState _sellState = new();

    public IReadOnlyList<GapSignalTriggerResult> ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config)
    {
        var normalizedConfirm = Math.Abs(config.ConfirmGapPts);
        var normalizedOpen = Math.Abs(config.OpenPts);
        var normalizedHoldMs = Math.Max(0, config.HoldConfirmMs);

        var results = new List<GapSignalTriggerResult>(capacity: 2);

        var buyResult = ProcessSide(
            side: GapSignalSide.Buy,
            action: GapSignalAction.Open,
            bid: snapshot.Bid,
            ask: snapshot.Ask,
            gap: snapshot.GapBuy,
            timestampUtc: snapshot.TimestampUtc,
            state: _buyState,
            holdConfirmMs: normalizedHoldMs,
            isConfirmSatisfied: value => value >= normalizedConfirm,
            isOpenSatisfied: value => value >= normalizedOpen);
        if (buyResult is not null)
        {
            results.Add(buyResult);
        }

        var sellResult = ProcessSide(
            side: GapSignalSide.Sell,
            action: GapSignalAction.Open,
            bid: snapshot.Bid,
            ask: snapshot.Ask,
            gap: snapshot.GapSell,
            timestampUtc: snapshot.TimestampUtc,
            state: _sellState,
            holdConfirmMs: normalizedHoldMs,
            isConfirmSatisfied: value => value <= -normalizedConfirm,
            isOpenSatisfied: value => value <= -normalizedOpen);
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

    internal static GapSignalTriggerResult? ProcessSide(
        GapSignalSide side,
        GapSignalAction action,
        decimal? bid,
        decimal? ask,
        int? gap,
        DateTime timestampUtc,
        SideWindowState state,
        int holdConfirmMs,
        Func<int, bool> isConfirmSatisfied,
        Func<int, bool> isOpenSatisfied)
    {
        if (!gap.HasValue || !isConfirmSatisfied(gap.Value))
        {
            state.Reset();
            return null;
        }

        if (!state.WindowStartUtc.HasValue)
        {
            state.WindowStartUtc = timestampUtc;
            state.Gaps.Clear();
        }

        state.LastTickUtc = timestampUtc;
        state.Gaps.Add(gap.Value);

        var elapsedMs = (timestampUtc - state.WindowStartUtc.Value).TotalMilliseconds;
        if (elapsedMs < holdConfirmMs)
        {
            return null;
        }

        if (state.Gaps.Count == 0 || state.Gaps.Any(v => !isConfirmSatisfied(v)))
        {
            state.Reset();
            return null;
        }

        var lastGap = state.Gaps[^1];
        if (!isOpenSatisfied(lastGap))
        {
            state.Reset();
            return null;
        }

        var result = new GapSignalTriggerResult(
            Triggered: true,
            Action: action,
            Side: side,
            Gaps: state.Gaps.ToArray(),
            TriggeredAtUtc: timestampUtc,
            TriggerPrice: side == GapSignalSide.Buy ? bid : ask);

        state.Reset();
        return result;
    }

    internal sealed class SideWindowState
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
