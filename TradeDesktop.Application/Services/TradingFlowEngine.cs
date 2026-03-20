using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class TradingFlowEngine(
    IOpenSignalEngine openSignalEngine,
    ICloseSignalEngine closeSignalEngine) : ITradingFlowEngine
{
    private readonly Random _random = new();

    public TradingFlowPhase CurrentPhase { get; private set; } = TradingFlowPhase.WaitingOpen;
    public TradingOpenMode CurrentOpenMode { get; private set; } = TradingOpenMode.None;
    public TradingPositionSide CurrentPositionSide { get; private set; } = TradingPositionSide.None;
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public int CurrentHoldingSeconds { get; private set; }
    public int CurrentWaitSeconds { get; private set; }

    public GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config)
    {
        if (CurrentPhase == TradingFlowPhase.WaitingOpen)
        {
            if (!CanCheckOpen(snapshot.TimestampUtc))
            {
                return null;
            }

            var openTrigger = openSignalEngine
                .ProcessSnapshot(snapshot, config)
                .FirstOrDefault(r => r.Triggered && r.Action == GapSignalAction.Open);

            if (openTrigger is null)
            {
                return null;
            }

            CurrentOpenMode = openTrigger.TriggerType == GapSignalTriggerType.OpenByGapBuy
                ? TradingOpenMode.GapBuy
                : TradingOpenMode.GapSell;

            CurrentPositionSide = openTrigger.PrimarySide == GapSignalSide.Buy
                ? TradingPositionSide.Buy
                : TradingPositionSide.Sell;

            OpenedAtUtc = openTrigger.TriggeredAtUtc;
            CurrentHoldingSeconds = NextSecondsInRange(config.StartTimeHold, config.EndTimeHold);
            CurrentPhase = CurrentOpenMode == TradingOpenMode.GapBuy
                ? TradingFlowPhase.WaitingCloseFromGapBuy
                : TradingFlowPhase.WaitingCloseFromGapSell;
            closeSignalEngine.Reset();
            openSignalEngine.Reset();
            return openTrigger;
        }

        if ((CurrentPhase != TradingFlowPhase.WaitingCloseFromGapBuy && CurrentPhase != TradingFlowPhase.WaitingCloseFromGapSell)
            || CurrentOpenMode == TradingOpenMode.None
            || CurrentPositionSide == TradingPositionSide.None)
        {
            return null;
        }

        if (!CanCheckClose(snapshot.TimestampUtc))
        {
            return null;
        }

        var closeTrigger = closeSignalEngine.ProcessSnapshot(snapshot, config, CurrentOpenMode);
        if (closeTrigger is null || !closeTrigger.Triggered || closeTrigger.Action != GapSignalAction.Close)
        {
            return null;
        }

        CurrentPhase = TradingFlowPhase.WaitingOpen;
        CurrentOpenMode = TradingOpenMode.None;
        CurrentPositionSide = TradingPositionSide.None;
        ClosedAtUtc = closeTrigger.TriggeredAtUtc;
        CurrentWaitSeconds = NextSecondsInRange(config.StartWaitTime, config.EndWaitTime);
        openSignalEngine.Reset();
        closeSignalEngine.Reset();
        return closeTrigger;
    }

    public void Reset()
    {
        CurrentPhase = TradingFlowPhase.WaitingOpen;
        CurrentOpenMode = TradingOpenMode.None;
        CurrentPositionSide = TradingPositionSide.None;
        OpenedAtUtc = null;
        ClosedAtUtc = null;
        CurrentHoldingSeconds = 0;
        CurrentWaitSeconds = 0;
        openSignalEngine.Reset();
        closeSignalEngine.Reset();
    }

    private bool CanCheckOpen(DateTime snapshotTimestampUtc)
    {
        if (!ClosedAtUtc.HasValue || CurrentWaitSeconds <= 0)
        {
            return true;
        }

        var elapsed = snapshotTimestampUtc - ClosedAtUtc.Value;
        return elapsed >= TimeSpan.FromSeconds(CurrentWaitSeconds);
    }

    private bool CanCheckClose(DateTime snapshotTimestampUtc)
    {
        if (!OpenedAtUtc.HasValue || CurrentHoldingSeconds <= 0)
        {
            return true;
        }

        var elapsed = snapshotTimestampUtc - OpenedAtUtc.Value;
        return elapsed >= TimeSpan.FromSeconds(CurrentHoldingSeconds);
    }

    private int NextSecondsInRange(int minSeconds, int maxSeconds)
    {
        var min = Math.Max(0, minSeconds);
        var max = Math.Max(0, maxSeconds);
        if (min > max)
        {
            (min, max) = (max, min);
        }

        if (min == max)
        {
            return min;
        }

        return _random.Next(min, max + 1);
    }
}
