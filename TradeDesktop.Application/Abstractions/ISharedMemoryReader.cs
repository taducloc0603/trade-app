using TradeDesktop.Application.Models;

namespace TradeDesktop.Application.Abstractions;

public interface ISharedMemoryReader : IMarketDataReader
{
    event EventHandler<SharedMemorySnapshot>? SnapshotReceived;
}