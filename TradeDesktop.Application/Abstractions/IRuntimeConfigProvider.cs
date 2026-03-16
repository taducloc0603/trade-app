using TradeDesktop.Domain.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IRuntimeConfigProvider
{
    string CurrentMachineHostName { get; }
    int CurrentPoint { get; }
    string CurrentMapName1 { get; }
    string CurrentMapName2 { get; }
    DashboardMetrics? CurrentDashboardMetrics { get; }
}
