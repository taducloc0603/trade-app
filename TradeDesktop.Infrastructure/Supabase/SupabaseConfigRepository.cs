using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.Supabase;

public sealed class SupabaseConfigRepository(HttpClient httpClient, string? supabaseUrl, string? supabaseKey) : IConfigRepository
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string? _supabaseUrl = supabaseUrl?.TrimEnd('/');
    private readonly string? _supabaseKey = supabaseKey;

    public async Task<ConfigRecord?> GetByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var normalizedIp = NormalizeCode(ip);

        var row = await GetFirstByColumnAsync("ip", normalizedIp, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var sansJson = row.Sans.ValueKind == JsonValueKind.Array
            ? row.Sans.GetRawText()
            : "[]";

        return new ConfigRecord(
            Id: string.IsNullOrWhiteSpace(row.Id) ? string.Empty : row.Id,
            Code: string.IsNullOrWhiteSpace(row.Code) ? (row.Id ?? string.Empty) : row.Code,
            SansJson: sansJson,
            Ip: row.Ip);
    }

    public async Task<bool> UpdateSansAndIpByIpAsync(string ip, string sansJson, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var normalizedIp = NormalizeCode(ip);

        return await UpdateByColumnAsync("ip", normalizedIp, sansJson, normalizedIp, cancellationToken);
    }

    private static string NormalizeCode(string code) => code.Trim();

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_supabaseUrl) &&
        !string.IsNullOrWhiteSpace(_supabaseKey);

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("apikey", _supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_supabaseKey}");
    }

    private async Task<ConfigRow?> GetFirstByColumnAsync(string columnName, string value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_supabaseUrl}/rest/v1/configs?select=id,code,sans,ip&{columnName}=eq.{Uri.EscapeDataString(value)}&limit=1");

        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase GetFirstByColumnAsync thất bại. Column={columnName}, Status={(int)response.StatusCode}, Body={errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = doc.RootElement[0];
        if (first.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        first.TryGetProperty("id", out var idElement);
        first.TryGetProperty("code", out var codeElement);
        first.TryGetProperty("sans", out var sansElement);

        // DB column name is lowercase: ip
        var hasIp = first.TryGetProperty("ip", out var ipElement);
        if (!hasIp)
        {
            first.TryGetProperty("Ip", out ipElement);
        }

        return new ConfigRow
        {
            Id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null,
            Code = codeElement.ValueKind == JsonValueKind.String ? codeElement.GetString() : null,
            // Clone để JsonElement không còn phụ thuộc JsonDocument đã dispose.
            Sans = sansElement.ValueKind is JsonValueKind.Undefined
                ? default
                : sansElement.Clone(),
            Ip = ipElement.ValueKind == JsonValueKind.String ? ipElement.GetString() : null
        };
    }

    private async Task<bool> UpdateByColumnAsync(
        string columnName,
        string value,
        string sansJson,
        string ip,
        CancellationToken cancellationToken)
    {
        using var sansDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sansJson) ? "[]" : sansJson);

        var payload = JsonSerializer.Serialize(new
        {
            sans = sansDoc.RootElement,
            ip = ip.Trim()
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{_supabaseUrl}/rest/v1/configs?{columnName}=eq.{Uri.EscapeDataString(value)}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        AddAuthHeaders(request);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase UpdateByColumnAsync thất bại. Column={columnName}, Status={(int)response.StatusCode}, Body={errorBody}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
    }

    private sealed class ConfigRow
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("sans")]
        public JsonElement Sans { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
    }
}
