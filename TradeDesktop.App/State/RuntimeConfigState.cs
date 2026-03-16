using TradeDesktop.Application.Abstractions;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.State;

public sealed class RuntimeConfigState : IRuntimeConfigProvider, IRuntimeConfigStateUpdater
{
    public string CurrentMachineHostName { get; private set; } = string.Empty;
    public int CurrentPoint { get; private set; }
    public string CurrentMapName1 { get; private set; } = string.Empty;
    public string CurrentMapName2 { get; private set; } = string.Empty;
    public DashboardMetrics? CurrentDashboardMetrics { get; private set; }

    // Backward-compatible aliases for existing bindings/usages.
    public string MachineHostName => CurrentMachineHostName;
    public string MapName1 => CurrentMapName1;
    public string MapName2 => CurrentMapName2;

    public event EventHandler? StateChanged;

    public void Update(string machineHostName, string mapName1, string mapName2, int point)
    {
        CurrentMachineHostName = (machineHostName ?? string.Empty).Trim().ToLower();
        CurrentPoint = point > 0 ? point : 1;
        CurrentMapName1 = (mapName1 ?? string.Empty).Trim();
        CurrentMapName2 = (mapName2 ?? string.Empty).Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(string machineHostName, string mapName1, string mapName2)
        => Update(machineHostName, mapName1, mapName2, CurrentPoint);

    public void UpdateDashboardMetrics(DashboardMetrics snapshot)
    {
        CurrentDashboardMetrics = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
