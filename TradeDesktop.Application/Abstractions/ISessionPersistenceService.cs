using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface ISessionPersistenceService
{
    Task<PersistedSession?> LoadAsync(CancellationToken ct);
    void EnqueueUpsertPair(PersistedPair pair);
    void EnqueueRemovePair(string pairId);
    void EnqueueUpsertWaitWindow(PersistedWaitWindow waitWindow);
    void EnqueueRemoveWaitWindow();
    Task FlushAsync(CancellationToken ct);
    Task AcquireInstanceLockAsync(CancellationToken ct);
    ValueTask ReleaseInstanceLockAsync();
}
