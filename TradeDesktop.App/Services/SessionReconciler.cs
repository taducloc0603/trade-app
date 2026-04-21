using TradeDesktop.App.Helpers;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.App.Services;

public sealed class SessionReconciler(
    ISessionPersistenceService sessionPersistence,
    ITradesSharedMemoryReader tradesSharedMemoryReader,
    RuntimeConfigState runtimeConfigState)
{
    public async Task<SessionReconcileResult> ReconcileAsync(CancellationToken ct)
    {
        var persisted = await sessionPersistence.LoadAsync(ct);
        if (persisted is null)
        {
            return SessionReconcileResult.Empty;
        }

        var tradeMapA = OrderMapNameResolver.BuildTradeMapName(runtimeConfigState.CurrentMapName1);
        var tradeMapB = OrderMapNameResolver.BuildTradeMapName(runtimeConfigState.CurrentMapName2);
        var left = tradesSharedMemoryReader.ReadTrades(tradeMapA);
        var right = tradesSharedMemoryReader.ReadTrades(tradeMapB);

        if (!left.IsMapAvailable || !left.IsParseSuccess || !right.IsMapAvailable || !right.IsParseSuccess)
        {
            return new SessionReconcileResult
            {
                IsSnapshotUnavailable = true
            };
        }

        if (!string.Equals((persisted.MapNameA ?? string.Empty).Trim(), tradeMapA.Trim(), StringComparison.Ordinal)
            || !string.Equals((persisted.MapNameB ?? string.Empty).Trim(), tradeMapB.Trim(), StringComparison.Ordinal))
        {
            return new SessionReconcileResult
            {
                IsMapMismatch = true,
                PersistedSession = persisted
            };
        }

        var restoredTickets = new HashSet<ulong>();
        var decisions = new List<ReconciledPairDecision>();

        foreach (var pair in persisted.Pairs)
        {
            var legA = left.Records.FirstOrDefault(x => x.Ticket == pair.LegA.Ticket);
            var legB = right.Records.FirstOrDefault(x => x.Ticket == pair.LegB.Ticket);

            var aOk = legA is not null && VerifyLeg(pair, pair.LegA, legA);
            var bOk = legB is not null && VerifyLeg(pair, pair.LegB, legB);

            if (aOk && bOk)
            {
                restoredTickets.Add(pair.LegA.Ticket);
                restoredTickets.Add(pair.LegB.Ticket);
                decisions.Add(new ReconciledPairDecision(pair, ReconciledPairStatus.Full));
                continue;
            }

            if (aOk)
            {
                restoredTickets.Add(pair.LegA.Ticket);
                decisions.Add(new ReconciledPairDecision(pair, ReconciledPairStatus.HalfA));
                sessionPersistence.EnqueueRemovePair(pair.PairId);
                continue;
            }

            if (bOk)
            {
                restoredTickets.Add(pair.LegB.Ticket);
                decisions.Add(new ReconciledPairDecision(pair, ReconciledPairStatus.HalfB));
                sessionPersistence.EnqueueRemovePair(pair.PairId);
                continue;
            }

            decisions.Add(new ReconciledPairDecision(pair, ReconciledPairStatus.Purged));
            sessionPersistence.EnqueueRemovePair(pair.PairId);
        }

        var orphans = left.Records
            .Select(x => new OrphanTrade("A", x.Ticket, x.Symbol, x.TradeType, x.Lot))
            .Concat(right.Records.Select(x => new OrphanTrade("B", x.Ticket, x.Symbol, x.TradeType, x.Lot)))
            .Where(x => !restoredTickets.Contains(x.Ticket))
            .ToList();

        var waitWindow = persisted.WaitWindow;
        if (waitWindow is not null)
        {
            var elapsed = DateTime.UtcNow - waitWindow.ClosedAtUtc;
            if (elapsed >= TimeSpan.FromSeconds(Math.Max(0, waitWindow.CurrentWaitSeconds)))
            {
                waitWindow = null;
                sessionPersistence.EnqueueRemoveWaitWindow();
            }
        }

        await sessionPersistence.FlushAsync(ct);

        return new SessionReconcileResult
        {
            PersistedSession = persisted,
            PairDecisions = decisions,
            Orphans = orphans,
            WaitWindowToRestore = waitWindow
        };
    }

    private static bool VerifyLeg(PersistedPair pair, PersistedPairLeg persistedLeg, TradeSharedRecord live)
    {
        if (!string.Equals((persistedLeg.Symbol ?? string.Empty).Trim(), (live.Symbol ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (persistedLeg.TradeType != live.TradeType)
        {
            return false;
        }

        if (Math.Abs(persistedLeg.Volume - live.Lot) > 1e-6)
        {
            return false;
        }

        var tolerance = Math.Abs(persistedLeg.OpenPrice) * 0.00001d;
        if (Math.Abs(persistedLeg.OpenPrice - live.Price) > Math.Max(0.00001d, tolerance))
        {
            return false;
        }

        var openAt = pair.OpenedAtUtc;
        var liveAt = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Min((ulong)long.MaxValue, live.TimeMsc)).UtcDateTime;
        return liveAt >= openAt.AddSeconds(-30) && liveAt <= openAt.AddSeconds(120);
    }
}

public sealed class SessionReconcileResult
{
    public static SessionReconcileResult Empty { get; } = new();

    public bool IsSnapshotUnavailable { get; init; }
    public bool IsMapMismatch { get; init; }
    public PersistedSession? PersistedSession { get; init; }
    public IReadOnlyList<ReconciledPairDecision> PairDecisions { get; init; } = [];
    public IReadOnlyList<OrphanTrade> Orphans { get; init; } = [];
    public PersistedWaitWindow? WaitWindowToRestore { get; init; }
}

public enum ReconciledPairStatus
{
    Full,
    HalfA,
    HalfB,
    Purged
}

public sealed record ReconciledPairDecision(PersistedPair Pair, ReconciledPairStatus Status);

public sealed record OrphanTrade(string Exchange, ulong Ticket, string Symbol, int TradeType, double Volume);
