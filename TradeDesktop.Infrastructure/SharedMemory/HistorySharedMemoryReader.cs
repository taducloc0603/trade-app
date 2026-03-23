using System.IO;
using System.IO.MemoryMappedFiles;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.SharedMemory;

public sealed class HistorySharedMemoryReader : IHistorySharedMemoryReader
{
    private const int HeaderSize = 16;
    private const int RecordSize = 28;

    public SharedMapReadResult<HistorySharedRecord> ReadHistory(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return SharedMapReadResult<HistorySharedRecord>.MapNotFound(string.Empty);
        }

        var normalizedMapName = mapName.Trim();

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(normalizedMapName, MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            var capacity = accessor.Capacity;
            if (capacity < HeaderSize)
            {
                return SharedMapReadResult<HistorySharedRecord>.ParseError("Lỗi parse dữ liệu: header không hợp lệ");
            }

            var rawCount = accessor.ReadInt32(0);
            var timestamp = accessor.ReadUInt64(4);
            var safeCount = Math.Max(0, rawCount);
            var maxCountByCapacity = (int)Math.Max(0, (capacity - HeaderSize) / RecordSize);
            var countToRead = Math.Min(safeCount, maxCountByCapacity);

            if (safeCount > maxCountByCapacity)
            {
                return SharedMapReadResult<HistorySharedRecord>.ParseError("Lỗi parse dữ liệu: count vượt quá kích thước map");
            }

            if (countToRead == 0)
            {
                return SharedMapReadResult<HistorySharedRecord>.Success(timestamp, Array.Empty<HistorySharedRecord>(), safeCount);
            }

            var records = new List<HistorySharedRecord>(countToRead);
            for (var i = 0; i < countToRead; i++)
            {
                var offset = HeaderSize + (i * RecordSize);
                records.Add(new HistorySharedRecord(
                    Ticket: accessor.ReadUInt64(offset + 0),
                    Profit: accessor.ReadDouble(offset + 8),
                    Volume: accessor.ReadDouble(offset + 16),
                    DealTime: accessor.ReadInt32(offset + 24)));
            }

            return SharedMapReadResult<HistorySharedRecord>.Success(timestamp, records, safeCount);
        }
        catch (FileNotFoundException)
        {
            return SharedMapReadResult<HistorySharedRecord>.MapNotFound(normalizedMapName);
        }
        catch (Exception ex)
        {
            return SharedMapReadResult<HistorySharedRecord>.ParseError($"Lỗi parse dữ liệu: {ex.Message}");
        }
    }
}