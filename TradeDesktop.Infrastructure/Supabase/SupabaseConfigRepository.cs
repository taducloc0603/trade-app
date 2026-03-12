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

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = NormalizeCode(code);

        // Theo rule nghiệp vụ hiện tại: ô Code trên UI map trực tiếp tới cột id trong DB.
        return await ExistsByColumnAsync("id", normalizedCode, cancellationToken);
    }

    public async Task<ConfigRecord?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalizedCode = NormalizeCode(code);

        var row = await GetFirstByColumnAsync("id", normalizedCode, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var sansJson = row.Sans.ValueKind == JsonValueKind.Array
            ? row.Sans.GetRawText()
            : "[]";

        return new ConfigRecord(
            string.IsNullOrWhiteSpace(row.Id) ? normalizedCode : row.Id,
            sansJson,
            row.Ip);
    }

    public async Task<bool> UpdateSansAndIpByCodeAsync(string code, string sansJson, string ip, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = NormalizeCode(code);

        return await UpdateByColumnAsync("id", normalizedCode, sansJson, ip, cancellationToken);
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

    private async Task<bool> ExistsByColumnAsync(string columnName, string value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_supabaseUrl}/rest/v1/configs?select={columnName}&{columnName}=eq.{Uri.EscapeDataString(value)}&limit=1");

        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase ExistsByColumnAsync thất bại. Column={columnName}, Status={(int)response.StatusCode}, Body={errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
    }

    private async Task<ConfigRow?> GetFirstByColumnAsync(string columnName, string value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_supabaseUrl}/rest/v1/configs?select=id,sans,ip&{columnName}=eq.{Uri.EscapeDataString(value)}&limit=1");

        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase GetFirstByColumnAsync thất bại. Column={columnName}, Status={(int)response.StatusCode}, Body={errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var rows = JsonSerializer.Deserialize<List<ConfigRow>>(json);
        return rows?.FirstOrDefault();
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

        return true;
    }

    private sealed class ConfigRow
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("sans")]
        public JsonElement Sans { get; set; }

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
    }
}
