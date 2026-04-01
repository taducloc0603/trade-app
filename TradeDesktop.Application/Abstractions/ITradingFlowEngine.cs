using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface ITradingFlowEngine
{
    TradingFlowPhase CurrentPhase { get; }
    TradingOpenMode CurrentOpenMode { get; }
    TradingPositionSide CurrentPositionSide { get; }
    DateTime? OpenedAtUtc { get; }
    DateTime? ClosedAtUtc { get; }
    int CurrentHoldingSeconds { get; }
    int CurrentWaitSeconds { get; }

    GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config);

    void BeginWaitAfterClose(
        DateTime closeCompletedAtUtc,
        int startWaitSeconds,
        int endWaitSeconds);

    void AbortPendingCloseExecution();

    void Reset();
}
