using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IRuntimeConfigStateUpdater
{
    void UpdateDashboardMetrics(SharedMemorySnapshot snapshot);
}
