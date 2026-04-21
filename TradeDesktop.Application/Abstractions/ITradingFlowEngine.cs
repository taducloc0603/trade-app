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
    int CurrentOpenQualifyingCount { get; }
    int CurrentCloseQualifyingCount { get; }

    GapSignalTriggerResult? ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config);

    void BeginWaitAfterClose(
        DateTime closeCompletedAtUtc,
        int startWaitSeconds,
        int endWaitSeconds);

    void AbortPendingCloseExecution();

    void AbortPendingOpenExecution();

    bool TryConsumeQualifyingForOpen(int requiredN);

    bool TryConsumeQualifyingForClose(int requiredN);

    void ResetQualifyingCounters();

    void ForceWaitingClose(TradingPositionSide positionSide);

    void ForceWaitingOpen();

    void RestoreWaitingClose(
        TradingFlowPhase phase,
        TradingOpenMode openMode,
        TradingPositionSide side,
        DateTime openedAtUtc,
        int holdingSeconds,
        int lastSeenStartHold,
        int lastSeenEndHold);

    void RestoreWaitingOpenWithWait(
        DateTime closedAtUtc,
        int waitSeconds,
        int lastSeenStartWait,
        int lastSeenEndWait,
        int lastSeenStartHold,
        int lastSeenEndHold);

    void Reset();
}
