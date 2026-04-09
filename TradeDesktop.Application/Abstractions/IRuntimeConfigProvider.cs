using TradeDesktop.Domain.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IRuntimeConfigProvider
{
    string CurrentMachineHostName { get; }
    int CurrentPoint { get; }
    int CurrentOpenPts { get; }
    int CurrentConfirmGapPts { get; }
    int CurrentHoldConfirmMs { get; }
    int CurrentClosePts { get; }
    int CurrentCloseConfirmGapPts { get; }
    int CurrentCloseHoldConfirmMs { get; }
    int CurrentStartTimeHold { get; }
    int CurrentEndTimeHold { get; }
    int CurrentStartWaitTime { get; }
    int CurrentEndWaitTime { get; }
    int CurrentConfirmLatencyMs { get; }
    int CurrentMaxGap { get; }
    int CurrentMaxSpread { get; }
    int CurrentOpenMaxTimesTick { get; }
    int CurrentCloseMaxTimesTick { get; }
    int CurrentOpenPendingTimeMs { get; }
    int CurrentClosePendingTimeMs { get; }
    string CurrentMapName1 { get; }
    string CurrentMapName2 { get; }
    DashboardMetrics? CurrentDashboardMetrics { get; }
}
