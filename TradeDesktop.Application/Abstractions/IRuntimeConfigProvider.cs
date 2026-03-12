using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IRuntimeConfigProvider
{
    string CurrentIp { get; }
    string CurrentCode { get; }
    string CurrentMapName1 { get; }
    string CurrentMapName2 { get; }
    SharedMemorySnapshot? CurrentDashboardMetrics { get; }
}
