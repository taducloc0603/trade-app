using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.Application.Services;

public interface IConfigService
{
    Task<ConfigLoadResult> LoadByLocalIpAsync(CancellationToken cancellationToken = default);
    Task<ConfigSaveResult> SaveByLocalIpAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default);
}

public sealed class ConfigService(IConfigRepository configRepository) : IConfigService
{
    public async Task<ConfigLoadResult> LoadByLocalIpAsync(CancellationToken cancellationToken = default)
    {
        var localIp = GetLocalIpAddress();
        if (string.IsNullOrWhiteSpace(localIp))
        {
            return ConfigLoadResult.Failed(string.Empty, "Không lấy được IP máy hiện tại.");
        }

        var record = await configRepository.GetByIpAsync(localIp, cancellationToken);
        if (record is null)
        {
            return ConfigLoadResult.NotFound(localIp);
        }

        var (mapName1, mapName2) = ParseSans(record.SansJson);
        return ConfigLoadResult.Success(localIp, mapName1, mapName2);
    }

    public async Task<ConfigSaveResult> SaveByLocalIpAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default)
    {
        var localIp = GetLocalIpAddress();
        if (string.IsNullOrWhiteSpace(localIp))
        {
            return ConfigSaveResult.Failed("Không lấy được IP máy hiện tại.");
        }

        var sansJson = BuildSans(mapName1, mapName2);

        var updated = await configRepository.UpdateSansAndIpByIpAsync(localIp, sansJson, cancellationToken);
        if (!updated)
        {
            return ConfigSaveResult.Failed("Lưu thất bại: không có bản ghi nào được cập nhật.");
        }

        var refreshed = await configRepository.GetByIpAsync(localIp, cancellationToken);
        if (refreshed is null)
        {
            return ConfigSaveResult.Failed("Đã gọi lưu nhưng không đọc lại được record để xác nhận giá trị ip.");
        }

        var savedIp = refreshed.Ip?.Trim() ?? string.Empty;
        if (!string.Equals(savedIp, localIp, StringComparison.OrdinalIgnoreCase))
        {
            return ConfigSaveResult.Failed(
                $"Lưu chưa hoàn tất: ip trong DB là '{savedIp}' nhưng ip local là '{localIp}'. Kiểm tra quyền update cột ip/RLS hoặc trigger DB.");
        }

        return ConfigSaveResult.Success(localIp);
    }

    private static (string MapName1, string MapName2) ParseSans(string? sansJson)
    {
        if (string.IsNullOrWhiteSpace(sansJson))
        {
            return (string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(sansJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, string.Empty);
            }

            var mapName1 = doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0].GetString() ?? string.Empty
                : string.Empty;

            var mapName2 = doc.RootElement.GetArrayLength() > 1
                ? doc.RootElement[1].GetString() ?? string.Empty
                : string.Empty;

            return (mapName1, mapName2);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string BuildSans(string? mapName1, string? mapName2)
    {
        var sans = new[]
        {
            mapName1?.Trim() ?? string.Empty,
            mapName2?.Trim() ?? string.Empty
        };

        return JsonSerializer.Serialize(sans);
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipv4 = host.AddressList.FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address));

            return ipv4?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}

public sealed record ConfigLoadResult(
    bool IsSuccess,
    bool Exists,
    string LocalIp,
    string MapName1,
    string MapName2,
    string? Error)
{
    public static ConfigLoadResult Success(string localIp, string mapName1, string mapName2) =>
        new(true, true, localIp, mapName1, mapName2, null);

    public static ConfigLoadResult NotFound(string localIp) =>
        new(false, false, localIp, string.Empty, string.Empty, null);

    public static ConfigLoadResult Failed(string localIp, string error) =>
        new(false, true, localIp, string.Empty, string.Empty, error);
}

public sealed record ConfigSaveResult(bool IsSuccess, string? LocalIp, string? Error)
{
    public static ConfigSaveResult Success(string localIp) => new(true, localIp, null);
    public static ConfigSaveResult Failed(string error) => new(false, null, error);
}