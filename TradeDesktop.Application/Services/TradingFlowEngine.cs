using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class TradingFlowEngine(
    IOpenSignalEngine openSignalEngine,
    ICloseSignalEngine closeSignalEngine) : ITradingFlowEngine
{
    public TradingFlowPhase CurrentPhase { get; private set; } = TradingFlowPhase.WaitingOpen;
    public TradingPositionSide CurrentPositionSide { get; private set; } = TradingPositionSide.None;

    public GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config)
    {
        if (CurrentPhase == TradingFlowPhase.WaitingOpen)
        {
            var openTrigger = openSignalEngine
                .ProcessSnapshot(snapshot, config)
                .FirstOrDefault(r => r.Triggered && r.Action == GapSignalAction.Open);

            if (openTrigger is null)
            {
                return null;
            }

            CurrentPositionSide = openTrigger.Side == GapSignalSide.Buy
                ? TradingPositionSide.Buy
                : TradingPositionSide.Sell;

            CurrentPhase = TradingFlowPhase.WaitingClose;
            closeSignalEngine.Reset();
            openSignalEngine.Reset();
            return openTrigger;
        }

        if (CurrentPhase != TradingFlowPhase.WaitingClose || CurrentPositionSide == TradingPositionSide.None)
        {
            return null;
        }

        var closeTrigger = closeSignalEngine.ProcessSnapshot(snapshot, config, CurrentPositionSide);
        if (closeTrigger is null || !closeTrigger.Triggered || closeTrigger.Action != GapSignalAction.Close)
        {
            return null;
        }

        CurrentPhase = TradingFlowPhase.WaitingOpen;
        CurrentPositionSide = TradingPositionSide.None;
        openSignalEngine.Reset();
        closeSignalEngine.Reset();
        return closeTrigger;
    }

    public void Reset()
    {
        CurrentPhase = TradingFlowPhase.WaitingOpen;
        CurrentPositionSide = TradingPositionSide.None;
        openSignalEngine.Reset();
        closeSignalEngine.Reset();
    }
}
