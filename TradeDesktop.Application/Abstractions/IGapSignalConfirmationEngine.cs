using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IGapSignalConfirmationEngine
{
    IReadOnlyList<GapSignalTriggerResult> ProcessSnapshot(
        GapSignalSnapshot snapshot,
        GapSignalConfirmationConfig config);

    IReadOnlyList<GapSignalDebugEvent> LastDebugEvents { get; }

    void Reset();
}