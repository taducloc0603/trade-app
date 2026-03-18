using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface ITradingFlowEngine
{
    TradingFlowPhase CurrentPhase { get; }
    TradingPositionSide CurrentPositionSide { get; }

    GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config);

    void Reset();
}
