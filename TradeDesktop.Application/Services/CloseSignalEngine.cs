using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Services;

public sealed class CloseSignalEngine : ICloseSignalEngine
{
    private readonly GapSignalConfirmationEngine.SideWindowState _buyState = new();
    private readonly GapSignalConfirmationEngine.SideWindowState _sellState = new();

    public GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config,
        TradingPositionSide positionSide)
    {
        var normalizedCloseConfirm = Math.Abs(config.CloseConfirmGapPts);
        var normalizedClose = Math.Abs(config.ClosePts);
        var normalizedHoldMs = Math.Max(0, config.CloseHoldConfirmMs);

        return positionSide switch
        {
            TradingPositionSide.Buy => GapSignalConfirmationEngine.ProcessSide(
                side: GapSignalSide.Buy,
                action: GapSignalAction.Close,
                gap: snapshot.GapSell,
                timestampUtc: snapshot.TimestampUtc,
                state: _buyState,
                holdConfirmMs: normalizedHoldMs,
                isConfirmSatisfied: value => value <= -normalizedCloseConfirm,
                isOpenSatisfied: value => value <= -normalizedClose),
            TradingPositionSide.Sell => GapSignalConfirmationEngine.ProcessSide(
                side: GapSignalSide.Sell,
                action: GapSignalAction.Close,
                gap: snapshot.GapBuy,
                timestampUtc: snapshot.TimestampUtc,
                state: _sellState,
                holdConfirmMs: normalizedHoldMs,
                isConfirmSatisfied: value => value >= normalizedCloseConfirm,
                isOpenSatisfied: value => value >= normalizedClose),
            _ => null
        };
    }

    public void Reset()
    {
        _buyState.Reset();
        _sellState.Reset();
    }
}
