using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.App.State;

public sealed class RuntimeConfigState : IRuntimeConfigProvider, IRuntimeConfigStateUpdater
{
    public string CurrentIp { get; private set; } = string.Empty;
    public string CurrentCode { get; private set; } = string.Empty;
    public string CurrentMapName1 { get; private set; } = string.Empty;
    public string CurrentMapName2 { get; private set; } = string.Empty;
    public SharedMemorySnapshot? CurrentDashboardMetrics { get; private set; }

    // Backward-compatible aliases for existing bindings/usages.
    public string LocalIp => CurrentIp;
    public string MapName1 => CurrentMapName1;
    public string MapName2 => CurrentMapName2;

    public event EventHandler? StateChanged;

    public void Update(string localIp, string code, string mapName1, string mapName2)
    {
        CurrentIp = (localIp ?? string.Empty).Trim();
        CurrentCode = (code ?? string.Empty).Trim();
        CurrentMapName1 = (mapName1 ?? string.Empty).Trim();
        CurrentMapName2 = (mapName2 ?? string.Empty).Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(string localIp, string mapName1, string mapName2)
        => Update(localIp, CurrentCode, mapName1, mapName2);

    public void UpdateDashboardMetrics(SharedMemorySnapshot snapshot)
    {
        CurrentDashboardMetrics = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
