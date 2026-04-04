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
    public int CurrentStartTimeHold { get; private set; }
    public int CurrentEndTimeHold { get; private set; }
    public int CurrentStartWaitTime { get; private set; }
    public int CurrentEndWaitTime { get; private set; }
    public int CurrentConfirmLatencyMs { get; private set; }
    public int CurrentMaxGap { get; private set; }
    public int CurrentMaxSpread { get; private set; }
    public int CurrentOpenPendingTimeMs { get; private set; } = 1000;
    public int CurrentClosePendingTimeMs { get; private set; } = 1000;
    public string CurrentMapName1 { get; private set; } = string.Empty;
    public string CurrentMapName2 { get; private set; } = string.Empty;
    public string CurrentPlatformA { get; private set; } = "mt5";
    public string CurrentPlatformB { get; private set; } = "mt5";
    public string CurrentChartHwndA { get; private set; } = string.Empty;
    public string CurrentTradeHwndA { get; private set; } = string.Empty;
    public string CurrentChartHwndB { get; private set; } = string.Empty;
    public string CurrentTradeHwndB { get; private set; } = string.Empty;
    public DashboardMetrics? CurrentDashboardMetrics { get; private set; }

    // Backward-compatible aliases for existing bindings/usages.
    public string MachineHostName => CurrentMachineHostName;
    public string MapName1 => CurrentMapName1;
    public string MapName2 => CurrentMapName2;
    public string PlatformA => CurrentPlatformA;
    public string PlatformB => CurrentPlatformB;
    public string ChartHwndA => CurrentChartHwndA;
    public string TradeHwndA => CurrentTradeHwndA;
    public string ChartHwndB => CurrentChartHwndB;
    public string TradeHwndB => CurrentTradeHwndB;
    public int OpenPts => CurrentOpenPts;
    public int ConfirmGapPts => CurrentConfirmGapPts;
    public int HoldConfirmMs => CurrentHoldConfirmMs;
    public int ClosePts => CurrentClosePts;
    public int CloseConfirmGapPts => CurrentCloseConfirmGapPts;
    public int CloseHoldConfirmMs => CurrentCloseHoldConfirmMs;
    public int StartTimeHold => CurrentStartTimeHold;
    public int EndTimeHold => CurrentEndTimeHold;
    public int StartWaitTime => CurrentStartWaitTime;
    public int EndWaitTime => CurrentEndWaitTime;
    public int ConfirmLatencyMs => CurrentConfirmLatencyMs;
    public int MaxGap => CurrentMaxGap;
    public int MaxSpread => CurrentMaxSpread;
    public int OpenPendingTimeMs => CurrentOpenPendingTimeMs;
    public int ClosePendingTimeMs => CurrentClosePendingTimeMs;

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
        int closeHoldConfirmMs,
        int startTimeHold,
        int endTimeHold,
        int startWaitTime,
        int endWaitTime,
        int confirmLatencyMs = 0,
        int maxGap = 0,
        int maxSpread = 0,
        int openPendingTimeMs = -1,
        int closePendingTimeMs = -1)
        => Update(
            machineHostName,
            mapName1,
            mapName2,
            CurrentPlatformA,
            CurrentPlatformB,
            point,
            openPts,
            confirmGapPts,
            holdConfirmMs,
            closePts,
            closeConfirmGapPts,
            closeHoldConfirmMs,
            startTimeHold,
            endTimeHold,
            startWaitTime,
            endWaitTime,
            confirmLatencyMs,
            maxGap,
            maxSpread,
            openPendingTimeMs,
            closePendingTimeMs);

    public void Update(
        string machineHostName,
        string mapName1,
        string mapName2,
        string platformA,
        string platformB,
        int point,
        int openPts,
        int confirmGapPts,
        int holdConfirmMs,
        int closePts,
        int closeConfirmGapPts,
        int closeHoldConfirmMs,
        int startTimeHold,
        int endTimeHold,
        int startWaitTime,
        int endWaitTime,
        int confirmLatencyMs = 0,
        int maxGap = 0,
        int maxSpread = 0,
        int openPendingTimeMs = -1,
        int closePendingTimeMs = -1)
    {
        CurrentMachineHostName = (machineHostName ?? string.Empty).Trim().ToLower();
        CurrentPoint = point > 0 ? point : 1;
        CurrentOpenPts = Math.Abs(openPts);
        CurrentConfirmGapPts = Math.Abs(confirmGapPts);
        CurrentHoldConfirmMs = Math.Max(0, holdConfirmMs);
        CurrentClosePts = Math.Abs(closePts);
        CurrentCloseConfirmGapPts = Math.Abs(closeConfirmGapPts);
        CurrentCloseHoldConfirmMs = Math.Max(0, closeHoldConfirmMs);
        CurrentStartTimeHold = Math.Max(0, startTimeHold);
        CurrentEndTimeHold = Math.Max(0, endTimeHold);
        CurrentStartWaitTime = Math.Max(0, startWaitTime);
        CurrentEndWaitTime = Math.Max(0, endWaitTime);
        CurrentConfirmLatencyMs = Math.Max(0, confirmLatencyMs);
        CurrentMaxGap = Math.Max(0, maxGap);
        CurrentMaxSpread = Math.Max(0, maxSpread);
        if (openPendingTimeMs >= 0)
        {
            CurrentOpenPendingTimeMs = Math.Max(0, openPendingTimeMs);
        }
        if (closePendingTimeMs >= 0)
        {
            CurrentClosePendingTimeMs = Math.Max(0, closePendingTimeMs);
        }
        CurrentMapName1 = (mapName1 ?? string.Empty).Trim();
        CurrentMapName2 = (mapName2 ?? string.Empty).Trim();
        CurrentPlatformA = NormalizePlatform(platformA);
        CurrentPlatformB = NormalizePlatform(platformB);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizePlatform(string? platform)
    {
        var normalized = (platform ?? string.Empty).Trim().ToLower();
        return normalized is "mt4" or "mt5" ? normalized : "mt5";
    }

    public void Update(string machineHostName, string mapName1, string mapName2, int point)
        => Update(
            machineHostName,
            mapName1,
            mapName2,
            CurrentPlatformA,
            CurrentPlatformB,
            point,
            CurrentOpenPts,
            CurrentConfirmGapPts,
            CurrentHoldConfirmMs,
            CurrentClosePts,
            CurrentCloseConfirmGapPts,
            CurrentCloseHoldConfirmMs,
            CurrentStartTimeHold,
            CurrentEndTimeHold,
            CurrentStartWaitTime,
            CurrentEndWaitTime,
            CurrentConfirmLatencyMs,
            CurrentMaxGap,
            CurrentMaxSpread,
            CurrentOpenPendingTimeMs,
            CurrentClosePendingTimeMs);

    public void Update(string machineHostName, string mapName1, string mapName2)
        => Update(
            machineHostName,
            mapName1,
            mapName2,
            CurrentPlatformA,
            CurrentPlatformB,
            CurrentPoint,
            CurrentOpenPts,
            CurrentConfirmGapPts,
            CurrentHoldConfirmMs,
            CurrentClosePts,
            CurrentCloseConfirmGapPts,
            CurrentCloseHoldConfirmMs,
            CurrentStartTimeHold,
            CurrentEndTimeHold,
            CurrentStartWaitTime,
            CurrentEndWaitTime,
            CurrentConfirmLatencyMs,
            CurrentMaxGap,
            CurrentMaxSpread,
            CurrentOpenPendingTimeMs,
            CurrentClosePendingTimeMs);

    public void UpdatePlatform(string platformA, string platformB)
    {
        CurrentPlatformA = NormalizePlatform(platformA);
        CurrentPlatformB = NormalizePlatform(platformB);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateDashboardMetrics(DashboardMetrics snapshot)
    {
        CurrentDashboardMetrics = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateManualTradeHwnd(string chartHwndA, string tradeHwndA, string chartHwndB, string tradeHwndB)
    {
        CurrentChartHwndA = (chartHwndA ?? string.Empty).Trim();
        CurrentTradeHwndA = (tradeHwndA ?? string.Empty).Trim();
        CurrentChartHwndB = (chartHwndB ?? string.Empty).Trim();
        CurrentTradeHwndB = (tradeHwndB ?? string.Empty).Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
