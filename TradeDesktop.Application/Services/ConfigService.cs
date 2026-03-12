using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.Application.Services;

public interface IConfigService
{
    Task<ConfigLoadResult> CheckAndLoadAsync(string inputCode, CancellationToken cancellationToken = default);
    Task<ConfigSaveResult> SaveAsync(string loadedCode, string mapName1, string mapName2, CancellationToken cancellationToken = default);
}

public sealed class ConfigService(IConfigRepository configRepository) : IConfigService
{
    public async Task<ConfigLoadResult> CheckAndLoadAsync(string inputCode, CancellationToken cancellationToken = default)
    {
        var normalizedCode = inputCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return ConfigLoadResult.NotFound();
        }

        var exists = await configRepository.ExistsByCodeAsync(normalizedCode, cancellationToken);
        if (!exists)
        {
            return ConfigLoadResult.NotFound();
        }

        var record = await configRepository.GetByCodeAsync(normalizedCode, cancellationToken);
        if (record is null)
        {
            return ConfigLoadResult.Failed("Không tải được config.");
        }

        var (mapName1, mapName2) = ParseSans(record.SansJson);
        return ConfigLoadResult.Success(record.Code.Trim(), mapName1, mapName2);
    }

    public async Task<ConfigSaveResult> SaveAsync(string loadedCode, string mapName1, string mapName2, CancellationToken cancellationToken = default)
    {
        var normalizedCode = loadedCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return ConfigSaveResult.Failed("Thiếu mã record để lưu.");
        }

        var sansJson = BuildSans(mapName1, mapName2);
        var localIp = GetLocalIpAddress();

        var updated = await configRepository.UpdateSansAndIpByCodeAsync(normalizedCode, sansJson, localIp, cancellationToken);
        return updated
            ? ConfigSaveResult.Success(localIp)
            : ConfigSaveResult.Failed("Lưu thất bại: không có bản ghi nào được cập nhật.");
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

public sealed record ConfigLoadResult(bool IsSuccess, bool Exists, string? LoadedCode, string MapName1, string MapName2, string? Error)
{
    public static ConfigLoadResult Success(string loadedCode, string mapName1, string mapName2) =>
        new(true, true, loadedCode, mapName1, mapName2, null);

    public static ConfigLoadResult NotFound() =>
        new(false, false, null, string.Empty, string.Empty, null);

    public static ConfigLoadResult Failed(string error) =>
        new(false, true, null, string.Empty, string.Empty, error);
}

public sealed record ConfigSaveResult(bool IsSuccess, string? LocalIp, string? Error)
{
    public static ConfigSaveResult Success(string localIp) => new(true, localIp, null);
    public static ConfigSaveResult Failed(string error) => new(false, null, error);
}