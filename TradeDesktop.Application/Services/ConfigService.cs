using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Helpers;

namespace TradeDesktop.Application.Services;

public interface IConfigService
{
    Task<ConfigLoadResult> LoadByMachineHostNameAsync(CancellationToken cancellationToken = default);
    Task<ConfigSaveResult> SaveByMachineHostNameAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default);
}

public sealed class ConfigService(
    IConfigRepository configRepository,
    IMachineIdentityService machineIdentityService) : IConfigService
{
    public async Task<ConfigLoadResult> LoadByMachineHostNameAsync(CancellationToken cancellationToken = default)
    {
        var hostName = machineIdentityService.GetHostName();
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return ConfigLoadResult.Failed(string.Empty, "Không lấy được host name máy hiện tại.");
        }

        var record = await configRepository.GetByHostNameAsync(hostName, cancellationToken);
        if (record is null)
        {
            return ConfigLoadResult.NotFound(hostName);
        }

        SansJsonHelper.TryParseSans(record.SansJson, out var mapName1, out var mapName2);
        return ConfigLoadResult.Success(
            hostName,
            mapName1,
            mapName2,
            record.Point,
            record.OpenPts,
            record.ConfirmGapPts,
            record.HoldConfirmMs,
            record.Id,
            record.SansJson);
    }

    public async Task<ConfigSaveResult> SaveByMachineHostNameAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default)
    {
        var hostName = machineIdentityService.GetHostName();
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return ConfigSaveResult.Failed("Không lấy được host name máy hiện tại.");
        }

        var sansJson = SansJsonHelper.BuildSans(mapName1, mapName2);

        var updated = await configRepository.UpdateSansAndHostNameByHostNameAsync(hostName, sansJson, cancellationToken);
        if (!updated)
        {
            return ConfigSaveResult.Failed("Lưu thất bại: không có bản ghi nào được cập nhật.");
        }

        var refreshed = await configRepository.GetByHostNameAsync(hostName, cancellationToken);
        if (refreshed is null)
        {
            return ConfigSaveResult.Failed("Đã gọi lưu nhưng không đọc lại được record để xác nhận giá trị hostname.");
        }

        var savedHostName = (refreshed.HostName ?? string.Empty).Trim().ToLower();
        if (!string.Equals(savedHostName, hostName, StringComparison.Ordinal))
        {
            return ConfigSaveResult.Failed(
                $"Lưu chưa hoàn tất: hostname trong DB là '{savedHostName}' nhưng hostname local là '{hostName}'. Kiểm tra quyền update cột hostname/RLS hoặc trigger DB.");
        }

        return ConfigSaveResult.Success(hostName);
    }
}

public sealed record ConfigLoadResult(
    bool IsSuccess,
    bool Exists,
    string MachineHostName,
    int Point,
    int OpenPts,
    int ConfirmGapPts,
    int HoldConfirmMs,
    string MapName1,
    string MapName2,
    string ConfigId,
    string SansJson,
    string? Error)
{
    public static ConfigLoadResult Success(
        string machineHostName,
        string mapName1,
        string mapName2,
        int point,
        int openPts,
        int confirmGapPts,
        int holdConfirmMs,
        string configId,
        string sansJson) =>
        new(
            true,
            true,
            machineHostName,
            point > 0 ? point : 1,
            Math.Abs(openPts),
            Math.Abs(confirmGapPts),
            Math.Max(0, holdConfirmMs),
            mapName1,
            mapName2,
            configId,
            sansJson,
            null);

    public static ConfigLoadResult NotFound(string machineHostName) =>
        new(false, false, machineHostName, 1, 0, 0, 0, string.Empty, string.Empty, string.Empty, "[]", null);

    public static ConfigLoadResult Failed(string machineHostName, string error) =>
        new(false, true, machineHostName, 1, 0, 0, 0, string.Empty, string.Empty, string.Empty, "[]", error);
}

public sealed record ConfigSaveResult(bool IsSuccess, string? MachineHostName, string? Error)
{
    public static ConfigSaveResult Success(string machineHostName) => new(true, machineHostName, null);
    public static ConfigSaveResult Failed(string error) => new(false, null, error);
}