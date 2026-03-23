using System.IO;
using System.IO.MemoryMappedFiles;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.SharedMemory;

public sealed class TradesSharedMemoryReader : ITradesSharedMemoryReader
{
    private const int HeaderSize = 16;
    private const int RecordSize = 56;

    public SharedMapReadResult<TradeSharedRecord> ReadTrades(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return SharedMapReadResult<TradeSharedRecord>.MapNotFound(string.Empty);
        }

        var normalizedMapName = mapName.Trim();

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(normalizedMapName, MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            var capacity = accessor.Capacity;
            if (capacity < HeaderSize)
            {
                return SharedMapReadResult<TradeSharedRecord>.ParseError("Lỗi parse dữ liệu: header không hợp lệ");
            }

            var rawCount = accessor.ReadInt32(0);
            var timestamp = accessor.ReadUInt64(4);
            var safeCount = Math.Max(0, rawCount);
            var maxCountByCapacity = (int)Math.Max(0, (capacity - HeaderSize) / RecordSize);
            var countToRead = Math.Min(safeCount, maxCountByCapacity);

            if (safeCount > maxCountByCapacity)
            {
                return SharedMapReadResult<TradeSharedRecord>.ParseError("Lỗi parse dữ liệu: count vượt quá kích thước map");
            }

            if (countToRead == 0)
            {
                return SharedMapReadResult<TradeSharedRecord>.Success(timestamp, Array.Empty<TradeSharedRecord>(), safeCount);
            }

            var records = new List<TradeSharedRecord>(countToRead);
            for (var i = 0; i < countToRead; i++)
            {
                var offset = HeaderSize + (i * RecordSize);
                records.Add(new TradeSharedRecord(
                    Ticket: accessor.ReadUInt64(offset + 0),
                    Lot: accessor.ReadDouble(offset + 8),
                    Price: accessor.ReadDouble(offset + 16),
                    Sl: accessor.ReadDouble(offset + 24),
                    Tp: accessor.ReadDouble(offset + 32),
                    Profit: accessor.ReadDouble(offset + 40),
                    TradeType: accessor.ReadInt32(offset + 48),
                    OpenTime: accessor.ReadInt32(offset + 52)));
            }

            return SharedMapReadResult<TradeSharedRecord>.Success(timestamp, records, safeCount);
        }
        catch (FileNotFoundException)
        {
            return SharedMapReadResult<TradeSharedRecord>.MapNotFound(normalizedMapName);
        }
        catch (Exception ex)
        {
            return SharedMapReadResult<TradeSharedRecord>.ParseError($"Lỗi parse dữ liệu: {ex.Message}");
        }
    }
}