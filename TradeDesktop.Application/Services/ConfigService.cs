using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Helpers;

namespace TradeDesktop.Application.Services;

public interface IConfigService
{
    Task<ConfigLoadResult> LoadByLocalIpAsync(CancellationToken cancellationToken = default);
    Task<ConfigSaveResult> SaveByLocalIpAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default);
}

public sealed class ConfigService(IConfigRepository configRepository) : IConfigService
{
    private static readonly HttpClient PublicIpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public async Task<ConfigLoadResult> LoadByLocalIpAsync(CancellationToken cancellationToken = default)
    {
        var candidateIps = GetLocalIpAddresses();
        if (candidateIps.Count == 0)
        {
            return ConfigLoadResult.Failed(string.Empty, "Không lấy được IP máy hiện tại.");
        }

        foreach (var localIp in candidateIps)
        {
            var record = await configRepository.GetByIpAsync(localIp, cancellationToken);
            if (record is null)
            {
                continue;
            }

            SansJsonHelper.TryParseSans(record.SansJson, out var mapName1, out var mapName2);
            return ConfigLoadResult.Success(
                localIp,
                record.Code,
                mapName1,
                mapName2,
                record.Point,
                record.Id,
                record.SansJson);
        }

        return ConfigLoadResult.NotFound(candidateIps[0]);
    }

    public async Task<ConfigSaveResult> SaveByLocalIpAsync(string mapName1, string mapName2, CancellationToken cancellationToken = default)
    {
        var candidateIps = GetLocalIpAddresses();
        if (candidateIps.Count == 0)
        {
            return ConfigSaveResult.Failed("Không lấy được IP máy hiện tại.");
        }

        string? localIp = null;
        foreach (var ip in candidateIps)
        {
            var existing = await configRepository.GetByIpAsync(ip, cancellationToken);
            if (existing is not null)
            {
                localIp = ip;
                break;
            }
        }

        localIp ??= candidateIps[0];

        var sansJson = SansJsonHelper.BuildSans(mapName1, mapName2);

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

    private static List<string> GetLocalIpAddresses()
    {
        var result = new List<string>();

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var privateIps = host.AddressList
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ip => IsPrivateIp(ip) ? 0 : 1)
                .ToList();

            result.AddRange(privateIps);
        }
        catch
        {
            // no-op, fallback below
        }

        if (!result.Contains("127.0.0.1", StringComparer.OrdinalIgnoreCase))
        {
            result.Add("127.0.0.1");
        }

        var publicIp = TryGetPublicIpAddress();
        if (!string.IsNullOrWhiteSpace(publicIp) && !result.Contains(publicIp, StringComparer.OrdinalIgnoreCase))
        {
            // Public IP hữu ích khi DB đang lưu IP WAN thay vì IP LAN.
            result.Insert(0, publicIp);
        }

        return result;
    }

    private static string? TryGetPublicIpAddress()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.ipify.org");
            using var response = PublicIpHttpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var ip = response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();
            return IPAddress.TryParse(ip, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork
                ? ip
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPrivateIp(string ip)
        => ip.StartsWith("10.", StringComparison.Ordinal) ||
           ip.StartsWith("192.168.", StringComparison.Ordinal) ||
           (ip.StartsWith("172.", StringComparison.Ordinal) &&
            int.TryParse(ip.Split('.')[1], out var secondOctet) &&
            secondOctet >= 16 && secondOctet <= 31);
}

public sealed record ConfigLoadResult(
    bool IsSuccess,
    bool Exists,
    string LocalIp,
    string Code,
    int Point,
    string MapName1,
    string MapName2,
    string ConfigId,
    string SansJson,
    string? Error)
{
    public static ConfigLoadResult Success(
        string localIp,
        string code,
        string mapName1,
        string mapName2,
        int point,
        string configId,
        string sansJson) =>
        new(true, true, localIp, code, point > 0 ? point : 1, mapName1, mapName2, configId, sansJson, null);

    public static ConfigLoadResult NotFound(string localIp) =>
        new(false, false, localIp, string.Empty, 1, string.Empty, string.Empty, string.Empty, "[]", null);

    public static ConfigLoadResult Failed(string localIp, string error) =>
        new(false, true, localIp, string.Empty, 1, string.Empty, string.Empty, string.Empty, "[]", error);
}

public sealed record ConfigSaveResult(bool IsSuccess, string? LocalIp, string? Error)
{
    public static ConfigSaveResult Success(string localIp) => new(true, localIp, null);
    public static ConfigSaveResult Failed(string error) => new(false, null, error);
}