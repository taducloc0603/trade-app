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
    string CurrentMapName1 { get; }
    string CurrentMapName2 { get; }
    DashboardMetrics? CurrentDashboardMetrics { get; }
}
