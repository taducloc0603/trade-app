using TradeDesktop.Application.Models;
using TradeDesktop.Infrastructure.Persistence;

namespace TradeDesktop.Tests;

public sealed class StatePersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonStatePersistence _sut;

    public StatePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TradeDesktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new JsonStatePersistence(_tempDir);
    }

    [Fact]
    public async Task SaveAsync_then_Load_returns_equal_snapshot()
    {
        var snapshot = BuildSnapshot();

        await _sut.SaveAsync(snapshot);
        var loaded = _sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot, loaded);
    }

    [Fact]
    public void Load_when_file_missing_returns_null()
    {
        var loaded = _sut.Load();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Load_when_primary_corrupt_falls_back_to_bak()
    {
        var snapshot = BuildSnapshot();
        await _sut.SaveAsync(snapshot);
        await _sut.SaveAsync(snapshot with { NextStt = snapshot.NextStt + 1 }); // create .bak

        var statePath = Path.Combine(_tempDir, "state.json");
        await File.WriteAllTextAsync(statePath, "{invalid json]");

        var loaded = _sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.SchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(snapshot.Hostname, loaded.Hostname);
        Assert.Equal(snapshot.NextStt, loaded.NextStt);
    }

    [Fact]
    public async Task SaveAsync_creates_bak_after_second_save()
    {
        await _sut.SaveAsync(BuildSnapshot());
        await _sut.SaveAsync(BuildSnapshot() with { NextStt = 99 });

        var bakPath = Path.Combine(_tempDir, "state.json.bak");
        Assert.True(File.Exists(bakPath));
    }

    [Fact]
    public async Task Clear_removes_all_files()
    {
        await _sut.SaveAsync(BuildSnapshot());
        await _sut.SaveAsync(BuildSnapshot() with { NextStt = 2 });

        _sut.Clear();

        Assert.False(File.Exists(Path.Combine(_tempDir, "state.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "state.json.bak")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "state.json.tmp")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // ignore test cleanup failures
        }
    }

    private static StateSnapshot BuildSnapshot()
        => new()
        {
            SavedAtUtc = new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc),
            Hostname = "TEST-HOST",
            Flow = new FlowSnapshot
            {
                Phase = TradingFlowPhase.WaitingCloseFromGapBuy,
                OpenMode = TradingOpenMode.GapBuy,
                PositionSide = TradingPositionSide.Buy,
                OpenedAtUtc = new DateTime(2026, 4, 20, 11, 59, 0, DateTimeKind.Utc),
                CurrentHoldingSeconds = 15,
                OpenQualifyingCount = 2,
                IsCloseExecutionPending = true,
                OpenedAtRuntimeUtc = new DateTime(2026, 4, 20, 11, 59, 5, DateTimeKind.Utc)
            },
            PendingOpenByMap = new Dictionary<string, List<PendingOpenRequestDto>>(StringComparer.Ordinal)
            {
                ["A_Trades"] =
                [
                    new PendingOpenRequestDto("AUTO-0001-1", "A_Trades", "EURUSD", 0, 0.1, 1.2345, 10,
                        DateTimeOffset.Parse("2026-04-20T19:00:00+07:00"), 1, 2, true, 1, "A")
                ]
            },
            PendingCloseByMap = new Dictionary<string, List<PendingCloseRequestDto>>(StringComparer.Ordinal)
            {
                ["B_Trades"] =
                [
                    new PendingCloseRequestDto("AUTO-0001-1", "B_Trades", 11UL, "EURUSD", 1, 0.1, 1.2350,
                        DateTimeOffset.Parse("2026-04-20T19:00:01+07:00"), 3, 4, true, 1, "B")
                ]
            },
            PairIdByTicket = new Dictionary<ulong, string> { [11UL] = "AUTO-0001-1" },
            PendingOpenPairById = new Dictionary<string, PendingOpenPairDto>(StringComparer.Ordinal)
            {
                ["AUTO-0001-1"] = new PendingOpenPairDto
                {
                    PairId = "AUTO-0001-1",
                    IsAutoFlow = true,
                    SlotNumber = 1,
                    CreatedAtLocal = DateTimeOffset.Parse("2026-04-20T19:00:00+07:00"),
                    OpenPendingTimeoutMs = 5000,
                    OpenConfirmedA = true,
                    OpenedTicketA = 11UL,
                    TradeMapNameA = "A_Trades",
                    TradeTypeA = 0,
                    SymbolA = "EURUSD",
                    VolumeA = 0.1
                }
            },
            PendingClosePairById = new Dictionary<string, PendingClosePairDto>(StringComparer.Ordinal)
            {
                ["AUTO-0001-1"] = new PendingClosePairDto
                {
                    PairId = "AUTO-0001-1",
                    IsAutoFlow = true,
                    SlotNumber = 1,
                    CreatedAtLocal = DateTimeOffset.Parse("2026-04-20T19:00:02+07:00"),
                    LastCheckedAtLocal = DateTimeOffset.Parse("2026-04-20T19:00:03+07:00"),
                    ClosePendingTimeoutMs = 5000,
                    TicketA = 11UL,
                    TradeTypeA = 0,
                    SymbolA = "EURUSD",
                    VolumeA = 0.1,
                    PlatformA = "Mt5",
                    TradeMapNameA = "A_Trades"
                }
            },
            ActiveAutoCycle = new ActiveAutoCycleDto(1, DateTimeOffset.Parse("2026-04-20T19:00:00+07:00"), "AUTO-0001-1", "AUTO-0001-1", 11UL, 12UL),
            ActiveAutoCloseRecoveryCycle = new ActiveAutoCycleDto(1, DateTimeOffset.Parse("2026-04-20T19:00:10+07:00"), "AUTO-0001-1", "AUTO-0001-1", 11UL, null),
            KnownTradeTicketsByMap = new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal) { ["A_Trades"] = [11UL] },
            KnownHistoryTicketsByMap = new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal) { ["A_History"] = [11UL] },
            LastTradeTimestampByMap = new Dictionary<string, ulong>(StringComparer.Ordinal) { ["A_Trades"] = 1000UL },
            LastHistoryTimestampByMap = new Dictionary<string, ulong>(StringComparer.Ordinal) { ["A_History"] = 1001UL },
            OpenExecutionMsByTicket = new Dictionary<ulong, long> { [11UL] = 150 },
            CloseExecutionMsByTicket = new Dictionary<ulong, long> { [11UL] = 230 },
            OpenSlippageByTicket = new Dictionary<ulong, double> { [11UL] = 0.5 },
            SttByPairId = new Dictionary<string, int>(StringComparer.Ordinal) { ["AUTO-0001-1"] = 1 },
            NextStt = 10,
            ManualSlot = 4,
            AutoSlot = 5,
            Invariant = new InvariantPauseState { IsPaused = true, ClearStreak = 2, PausedAtUtc = new DateTime(2026, 4, 20, 11, 59, 55, DateTimeKind.Utc) },
            LastAutoOpenClickAtLocal = DateTimeOffset.Parse("2026-04-20T19:00:05+07:00"),
            AutoOpenInFlight = 0,
            AutoCloseInFlight = 0,
            IsManualOpenInFlight = false,
            HadBothOpenRecently = true,
            ExternalPartialCloseStreak = 1,
            ExternalPartialCloseInFlight = false,
            CloseBothFlatPollStreak = 1
        };
}