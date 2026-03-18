using TradeDesktop.Application.Abstractions;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.State;

public sealed class RuntimeConfigState : IRuntimeConfigProvider, IRuntimeConfigStateUpdater
{
    public string CurrentMachineHostName { get; private set; } = string.Empty;
    public int CurrentPoint { get; private set; }
    public int CurrentOpenPts { get; private set; }
    public int CurrentConfirmGapPts { get; private set; }
    public int CurrentHoldConfirmMs { get; private set; }
    public int CurrentClosePts { get; private set; }
    public int CurrentCloseConfirmGapPts { get; private set; }
    public int CurrentCloseHoldConfirmMs { get; private set; }
    public string CurrentMapName1 { get; private set; } = string.Empty;
    public string CurrentMapName2 { get; private set; } = string.Empty;
    public DashboardMetrics? CurrentDashboardMetrics { get; private set; }

    // Backward-compatible aliases for existing bindings/usages.
    public string MachineHostName => CurrentMachineHostName;
    public string MapName1 => CurrentMapName1;
    public string MapName2 => CurrentMapName2;
    public int OpenPts => CurrentOpenPts;
    public int ConfirmGapPts => CurrentConfirmGapPts;
    public int HoldConfirmMs => CurrentHoldConfirmMs;
    public int ClosePts => CurrentClosePts;
    public int CloseConfirmGapPts => CurrentCloseConfirmGapPts;
    public int CloseHoldConfirmMs => CurrentCloseHoldConfirmMs;

    public event EventHandler? StateChanged;

    public void Update(
        string machineHostName,
        string mapName1,
        string mapName2,
        int point,
        int openPts,
        int confirmGapPts,
        int holdConfirmMs,
        int closePts,
        int closeConfirmGapPts,
        int closeHoldConfirmMs)
    {
        CurrentMachineHostName = (machineHostName ?? string.Empty).Trim().ToLower();
        CurrentPoint = point > 0 ? point : 1;
        CurrentOpenPts = Math.Abs(openPts);
        CurrentConfirmGapPts = Math.Abs(confirmGapPts);
        CurrentHoldConfirmMs = Math.Max(0, holdConfirmMs);
        CurrentClosePts = Math.Abs(closePts);
        CurrentCloseConfirmGapPts = Math.Abs(closeConfirmGapPts);
        CurrentCloseHoldConfirmMs = Math.Max(0, closeHoldConfirmMs);
        CurrentMapName1 = (mapName1 ?? string.Empty).Trim();
        CurrentMapName2 = (mapName2 ?? string.Empty).Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(string machineHostName, string mapName1, string mapName2, int point)
        => Update(
            machineHostName,
            mapName1,
            mapName2,
            point,
            CurrentOpenPts,
            CurrentConfirmGapPts,
            CurrentHoldConfirmMs,
            CurrentClosePts,
            CurrentCloseConfirmGapPts,
            CurrentCloseHoldConfirmMs);

    public void Update(string machineHostName, string mapName1, string mapName2)
        => Update(
            machineHostName,
            mapName1,
            mapName2,
            CurrentPoint,
            CurrentOpenPts,
            CurrentConfirmGapPts,
            CurrentHoldConfirmMs,
            CurrentClosePts,
            CurrentCloseConfirmGapPts,
            CurrentCloseHoldConfirmMs);

    public void UpdateDashboardMetrics(DashboardMetrics snapshot)
    {
        CurrentDashboardMetrics = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
