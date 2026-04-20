using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface IStatePersistence
{
    Task SaveAsync(StateSnapshot snapshot, CancellationToken ct = default);
    StateSnapshot? Load();
    void Clear();
}