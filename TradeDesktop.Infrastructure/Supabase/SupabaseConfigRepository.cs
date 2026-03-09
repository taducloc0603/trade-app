using System.Net.Http;
using System.Text;
using System.Text.Json;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.Infrastructure.Supabase;

public sealed class SupabaseConfigRepository(HttpClient httpClient, string? supabaseUrl, string? supabaseKey) : IConfigRepository
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string? _supabaseUrl = supabaseUrl?.TrimEnd('/');
    private readonly string? _supabaseKey = supabaseKey;

    public async Task<bool> ExistsByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var normalizedId = NormalizeId(id);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_supabaseUrl}/rest/v1/configs?select=id&id=eq.{Uri.EscapeDataString(normalizedId)}&limit=1");

        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase ExistsByIdAsync thất bại. Status={(int)response.StatusCode}, Body={errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
    }

    public async Task<bool> UpdateSansAsync(string id, string mapName1, string mapName2, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var normalizedId = NormalizeId(id);

        var payload = JsonSerializer.Serialize(new
        {
            sans = new[] { mapName1, mapName2 }
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{_supabaseUrl}/rest/v1/configs?id=eq.{Uri.EscapeDataString(normalizedId)}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        AddAuthHeaders(request);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static string NormalizeId(string id)
    {
        var trimmed = id.Trim();
        return Guid.TryParse(trimmed, out var guid)
            ? guid.ToString("D")
            : trimmed;
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_supabaseUrl) &&
        !string.IsNullOrWhiteSpace(_supabaseKey);

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("apikey", _supabaseKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_supabaseKey}");
    }
}
