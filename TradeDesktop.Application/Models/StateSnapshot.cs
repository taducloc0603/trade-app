namespace TradeDesktop.Application.Models;

public sealed record StateSnapshot
{
    public int SchemaVersion { get; init; } = 2;
    public DateTime SavedAtUtc { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public bool WasTradingLogicEnabled { get; init; }

    public FlowSnapshot Flow { get; init; } = new();
    public Dictionary<string, List<PendingOpenRequestDto>> PendingOpenByMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<PendingCloseRequestDto>> PendingCloseByMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<ulong, string> PairIdByTicket { get; init; } = new();
    public Dictionary<string, PendingOpenPairDto> PendingOpenPairById { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, PendingClosePairDto> PendingClosePairById { get; init; } = new(StringComparer.Ordinal);
    public ActiveAutoCycleDto? ActiveAutoCycle { get; init; }
    public ActiveAutoCycleDto? ActiveAutoCloseRecoveryCycle { get; init; }

    public Dictionary<string, HashSet<ulong>> KnownTradeTicketsByMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<ulong>> KnownHistoryTicketsByMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, ulong> LastTradeTimestampByMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, ulong> LastHistoryTimestampByMap { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<ulong, long> OpenExecutionMsByTicket { get; init; } = new();
    public Dictionary<ulong, long> CloseExecutionMsByTicket { get; init; } = new();
    public Dictionary<ulong, double> OpenSlippageByTicket { get; init; } = new();

    public Dictionary<string, int> SttByPairId { get; init; } = new(StringComparer.Ordinal);
    public int NextStt { get; init; }
    public int ManualSlot { get; init; }
    public int AutoSlot { get; init; }

    public InvariantPauseState Invariant { get; init; } = new();
    public DateTimeOffset? LastAutoOpenClickAtLocal { get; init; }
    public int AutoOpenInFlight { get; init; }
    public int AutoCloseInFlight { get; init; }
    public bool IsManualOpenInFlight { get; init; }
    public bool HadBothOpenRecently { get; init; }
    public int ExternalPartialCloseStreak { get; init; }
    public bool ExternalPartialCloseInFlight { get; init; }
    public int CloseBothFlatPollStreak { get; init; }
}

public sealed record FlowSnapshot
{
    public TradingFlowPhase Phase { get; init; }
    public TradingOpenMode OpenMode { get; init; }
    public TradingPositionSide PositionSide { get; init; }
    public DateTime? OpenedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public int CurrentHoldingSeconds { get; init; }
    public int CurrentWaitSeconds { get; init; }
    public int OpenQualifyingCount { get; init; }
    public int CloseQualifyingCount { get; init; }
    public bool IsCloseExecutionPending { get; init; }
    public DateTime? OpenedAtRuntimeUtc { get; init; }
    public DateTime? ClosedAtRuntimeUtc { get; init; }
}

public sealed record PendingOpenRequestDto(
    string PairId,
    string TradeMapName,
    string? Symbol,
    int TradeType,
    double? Volume,
    double? ExpectedPrice,
    int HoldingSeconds,
    DateTimeOffset AppOpenRequestTimeLocal,
    long AppOpenRequestUnixMs,
    long AppOpenRequestRawMs,
    bool IsAutoFlow,
    int SlotNumber,
    string ExchangeLabel);

public sealed record PendingCloseRequestDto(
    string PairId,
    string TradeMapName,
    ulong? Ticket,
    string? Symbol,
    int TradeType,
    double? Volume,
    double? ExpectedPrice,
    DateTimeOffset AppCloseRequestTimeLocal,
    long AppCloseRequestUnixMs,
    long AppCloseRequestRawMs,
    bool IsAutoFlow,
    int SlotNumber,
    string ExchangeLabel);

public sealed record PendingOpenPairDto
{
    public string PairId { get; init; } = string.Empty;
    public bool IsAutoFlow { get; init; }
    public int SlotNumber { get; init; }
    public DateTimeOffset CreatedAtLocal { get; init; }
    public int OpenPendingTimeoutMs { get; init; }
    public bool OpenConfirmedA { get; init; }
    public bool OpenConfirmedB { get; init; }
    public ulong? OpenedTicketA { get; init; }
    public ulong? OpenedTicketB { get; init; }
    public string? TradeMapNameA { get; init; }
    public string? TradeMapNameB { get; init; }
    public int? TradeTypeA { get; init; }
    public int? TradeTypeB { get; init; }
    public string? SymbolA { get; init; }
    public string? SymbolB { get; init; }
    public double? VolumeA { get; init; }
    public double? VolumeB { get; init; }
    public bool TimeoutCloseTriggered { get; init; }
    public bool TimeoutRecheckPending { get; init; }
    public DateTimeOffset? TimeoutRecheckRequestedAtLocal { get; init; }
    public bool IsResolved { get; init; }
}

public sealed record PendingClosePairDto
{
    public string PairId { get; init; } = string.Empty;
    public bool IsAutoFlow { get; init; }
    public int SlotNumber { get; init; }
    public DateTimeOffset CreatedAtLocal { get; init; }
    public DateTimeOffset LastCheckedAtLocal { get; init; }
    public int ClosePendingTimeoutMs { get; init; }
    public int RetryChecks { get; init; }
    public bool ExhaustedLogged { get; init; }
    public bool IsResolved { get; init; }
    public bool CloseConfirmedA { get; init; }
    public bool CloseConfirmedB { get; init; }
    public string? TradeMapNameA { get; init; }
    public string? TradeMapNameB { get; init; }
    public string? PlatformA { get; init; }
    public string? PlatformB { get; init; }
    public string? TradeHwndA { get; init; }
    public string? TradeHwndB { get; init; }
    public ulong? TicketA { get; init; }
    public ulong? TicketB { get; init; }
    public int? TradeTypeA { get; init; }
    public int? TradeTypeB { get; init; }
    public string? SymbolA { get; init; }
    public string? SymbolB { get; init; }
    public double? VolumeA { get; init; }
    public double? VolumeB { get; init; }
}

public sealed record ActiveAutoCycleDto(
    int Slot,
    DateTimeOffset OpenedAtLocal,
    string? PairIdA,
    string? PairIdB,
    ulong? TicketA,
    ulong? TicketB);

public sealed record InvariantPauseState
{
    public bool IsPaused { get; init; }
    public int ClearStreak { get; init; }
    public DateTime? PausedAtUtc { get; init; }
}