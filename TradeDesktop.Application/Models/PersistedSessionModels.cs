namespace TradeDesktop.Application.Models;

public sealed class PersistedSession
{
    public int SchemaVersion { get; set; } = 1;
    public string HostName { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; }
    public string MapNameA { get; set; } = string.Empty;
    public string MapNameB { get; set; } = string.Empty;
    public List<PersistedPair> Pairs { get; set; } = [];
    public PersistedWaitWindow? WaitWindow { get; set; }
}

public sealed class PersistedPair
{
    public string PairId { get; set; } = string.Empty;
    public bool IsAutoFlow { get; set; }
    public int SlotNumber { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public int CurrentHoldingSeconds { get; set; }
    public TradingFlowPhase CurrentPhase { get; set; } = TradingFlowPhase.WaitingOpen;
    public TradingPositionSide CurrentPositionSide { get; set; } = TradingPositionSide.None;
    public TradingOpenMode CurrentOpenMode { get; set; } = TradingOpenMode.None;
    public int LastSeenStartTimeHold { get; set; }
    public int LastSeenEndTimeHold { get; set; }
    public int Stt { get; set; }
    public PersistedPairLeg LegA { get; set; } = new();
    public PersistedPairLeg LegB { get; set; } = new();
}

public sealed class PersistedPairLeg
{
    public string MapName { get; set; } = string.Empty;
    public ulong Ticket { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public double Volume { get; set; }
    public int TradeType { get; set; }
    public double OpenPrice { get; set; }
    public ulong OpenEaTimeLocal { get; set; }
    public long AppOpenRequestRawMs { get; set; }
    public string Platform { get; set; } = string.Empty;
    public long? OpenExecutionMs { get; set; }
}

public sealed class PersistedWaitWindow
{
    public string PreviousPairId { get; set; } = string.Empty;
    public DateTime ClosedAtUtc { get; set; }
    public int CurrentWaitSeconds { get; set; }
    public int LastSeenStartWaitTime { get; set; }
    public int LastSeenEndWaitTime { get; set; }
    public int LastSeenStartTimeHold { get; set; }
    public int LastSeenEndTimeHold { get; set; }
}
