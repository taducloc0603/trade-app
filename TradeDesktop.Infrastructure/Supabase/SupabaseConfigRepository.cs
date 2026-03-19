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

    public async Task<ConfigRecord?> GetByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(hostName))
        {
            return null;
        }

        var normalizedHostName = NormalizeHostName(hostName);

        var row = await GetFirstByColumnAsync("hostname", normalizedHostName, cancellationToken)
                  ?? await GetFirstByColumnLikeAsync("hostname", normalizedHostName, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var sansJson = row.Sans.ValueKind == JsonValueKind.Array
            ? row.Sans.GetRawText()
            : "[]";

        return new ConfigRecord(
            Id: string.IsNullOrWhiteSpace(row.Id) ? string.Empty : row.Id,
            SansJson: sansJson,
            HostName: row.HostName,
            Point: row.Point > 0 ? row.Point : 1,
            OpenPts: row.OpenPts,
            ConfirmGapPts: row.ConfirmGapPts,
            HoldConfirmMs: row.HoldConfirmMs,
            ClosePts: row.ClosePts,
            CloseConfirmGapPts: row.CloseConfirmGapPts,
            CloseHoldConfirmMs: row.CloseHoldConfirmMs,
            StartTimeHold: row.StartTimeHold,
            EndTimeHold: row.EndTimeHold,
            StartWaitTime: row.StartWaitTime,
            EndWaitTime: row.EndWaitTime);
    }

    public async Task<bool> UpdateSansAndHostNameByHostNameAsync(string hostName, string sansJson, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Thiếu SUPABASE_URL hoặc SUPABASE_KEY/SUPABASE_ANON_KEY.");
        }

        if (string.IsNullOrWhiteSpace(hostName))
        {
            return false;
        }

        var normalizedHostName = NormalizeHostName(hostName);

        return await UpdateByColumnAsync("hostname", normalizedHostName, sansJson, normalizedHostName, cancellationToken);
    }

    private static string NormalizeHostName(string hostName) => hostName.Trim().ToLower();

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
            $"{_supabaseUrl}/rest/v1/configs?select=*&{columnName}=eq.{Uri.EscapeDataString(value)}&limit=1");

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
        first.TryGetProperty("sans", out var sansElement);
        first.TryGetProperty("point", out var pointElement);
        first.TryGetProperty("open_pts", out var openPtsElement);
        first.TryGetProperty("open_confirm_gap_pts", out var confirmGapPtsElement);
        first.TryGetProperty("open_hold_confirm_ms", out var holdConfirmMsElement);
        first.TryGetProperty("close_pts", out var closePtsElement);
        first.TryGetProperty("close_confirm_gap_pts", out var closeConfirmGapPtsElement);
        first.TryGetProperty("close_hold_confirm_ms", out var closeHoldConfirmMsElement);
        first.TryGetProperty("start_time_hold", out var startTimeHoldElement);
        first.TryGetProperty("end_time_hold", out var endTimeHoldElement);
        first.TryGetProperty("start_wait_time", out var startWaitTimeElement);
        first.TryGetProperty("end_wait_time", out var endWaitTimeElement);

        // DB column name is lowercase: hostname
        var hasHostName = first.TryGetProperty("hostname", out var hostNameElement);
        if (!hasHostName)
        {
            first.TryGetProperty("HostName", out hostNameElement);
        }

        return new ConfigRow
        {
            Id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null,
            // Clone để JsonElement không còn phụ thuộc JsonDocument đã dispose.
            Sans = sansElement.ValueKind is JsonValueKind.Undefined
                ? default
                : sansElement.Clone(),
            HostName = hostNameElement.ValueKind == JsonValueKind.String ? hostNameElement.GetString() : null,
            Point = pointElement.ValueKind == JsonValueKind.Number && pointElement.TryGetInt32(out var p) ? p : 1,
            OpenPts = openPtsElement.ValueKind == JsonValueKind.Number && openPtsElement.TryGetInt32(out var openPts) ? openPts : 0,
            ConfirmGapPts = confirmGapPtsElement.ValueKind == JsonValueKind.Number && confirmGapPtsElement.TryGetInt32(out var confirmGapPts) ? confirmGapPts : 0,
            HoldConfirmMs = holdConfirmMsElement.ValueKind == JsonValueKind.Number && holdConfirmMsElement.TryGetInt32(out var holdConfirmMs) ? holdConfirmMs : 0,
            ClosePts = closePtsElement.ValueKind == JsonValueKind.Number && closePtsElement.TryGetInt32(out var closePts) ? closePts : 0,
            CloseConfirmGapPts = closeConfirmGapPtsElement.ValueKind == JsonValueKind.Number && closeConfirmGapPtsElement.TryGetInt32(out var closeConfirmGapPts) ? closeConfirmGapPts : 0,
            CloseHoldConfirmMs = closeHoldConfirmMsElement.ValueKind == JsonValueKind.Number && closeHoldConfirmMsElement.TryGetInt32(out var closeHoldConfirmMs) ? closeHoldConfirmMs : 0,
            StartTimeHold = startTimeHoldElement.ValueKind == JsonValueKind.Number && startTimeHoldElement.TryGetInt32(out var startTimeHold) ? startTimeHold : 0,
            EndTimeHold = endTimeHoldElement.ValueKind == JsonValueKind.Number && endTimeHoldElement.TryGetInt32(out var endTimeHold) ? endTimeHold : 0,
            StartWaitTime = startWaitTimeElement.ValueKind == JsonValueKind.Number && startWaitTimeElement.TryGetInt32(out var startWaitTime) ? startWaitTime : 0,
            EndWaitTime = endWaitTimeElement.ValueKind == JsonValueKind.Number && endWaitTimeElement.TryGetInt32(out var endWaitTime) ? endWaitTime : 0
        };
    }

    private async Task<ConfigRow?> GetFirstByColumnLikeAsync(string columnName, string value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_supabaseUrl}/rest/v1/configs?select=*&{columnName}=ilike.*{Uri.EscapeDataString(value)}*&limit=1");

        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase GetFirstByColumnLikeAsync thất bại. Column={columnName}, Status={(int)response.StatusCode}, Body={errorBody}");
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
        first.TryGetProperty("sans", out var sansElement);
        first.TryGetProperty("point", out var pointElement);
        first.TryGetProperty("open_pts", out var openPtsElement);
        first.TryGetProperty("open_confirm_gap_pts", out var confirmGapPtsElement);
        first.TryGetProperty("open_hold_confirm_ms", out var holdConfirmMsElement);
        first.TryGetProperty("close_pts", out var closePtsElement);
        first.TryGetProperty("close_confirm_gap_pts", out var closeConfirmGapPtsElement);
        first.TryGetProperty("close_hold_confirm_ms", out var closeHoldConfirmMsElement);
        first.TryGetProperty("start_time_hold", out var startTimeHoldElement);
        first.TryGetProperty("end_time_hold", out var endTimeHoldElement);
        first.TryGetProperty("start_wait_time", out var startWaitTimeElement);
        first.TryGetProperty("end_wait_time", out var endWaitTimeElement);

        var hasHostName = first.TryGetProperty("hostname", out var hostNameElement);
        if (!hasHostName)
        {
            first.TryGetProperty("HostName", out hostNameElement);
        }

        return new ConfigRow
        {
            Id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null,
            Sans = sansElement.ValueKind is JsonValueKind.Undefined
                ? default
                : sansElement.Clone(),
            HostName = hostNameElement.ValueKind == JsonValueKind.String ? hostNameElement.GetString() : null,
            Point = pointElement.ValueKind == JsonValueKind.Number && pointElement.TryGetInt32(out var p) ? p : 1,
            OpenPts = openPtsElement.ValueKind == JsonValueKind.Number && openPtsElement.TryGetInt32(out var openPts) ? openPts : 0,
            ConfirmGapPts = confirmGapPtsElement.ValueKind == JsonValueKind.Number && confirmGapPtsElement.TryGetInt32(out var confirmGapPts) ? confirmGapPts : 0,
            HoldConfirmMs = holdConfirmMsElement.ValueKind == JsonValueKind.Number && holdConfirmMsElement.TryGetInt32(out var holdConfirmMs) ? holdConfirmMs : 0,
            ClosePts = closePtsElement.ValueKind == JsonValueKind.Number && closePtsElement.TryGetInt32(out var closePts) ? closePts : 0,
            CloseConfirmGapPts = closeConfirmGapPtsElement.ValueKind == JsonValueKind.Number && closeConfirmGapPtsElement.TryGetInt32(out var closeConfirmGapPts) ? closeConfirmGapPts : 0,
            CloseHoldConfirmMs = closeHoldConfirmMsElement.ValueKind == JsonValueKind.Number && closeHoldConfirmMsElement.TryGetInt32(out var closeHoldConfirmMs) ? closeHoldConfirmMs : 0,
            StartTimeHold = startTimeHoldElement.ValueKind == JsonValueKind.Number && startTimeHoldElement.TryGetInt32(out var startTimeHold) ? startTimeHold : 0,
            EndTimeHold = endTimeHoldElement.ValueKind == JsonValueKind.Number && endTimeHoldElement.TryGetInt32(out var endTimeHold) ? endTimeHold : 0,
            StartWaitTime = startWaitTimeElement.ValueKind == JsonValueKind.Number && startWaitTimeElement.TryGetInt32(out var startWaitTime) ? startWaitTime : 0,
            EndWaitTime = endWaitTimeElement.ValueKind == JsonValueKind.Number && endWaitTimeElement.TryGetInt32(out var endWaitTime) ? endWaitTime : 0
        };
    }

    private async Task<bool> UpdateByColumnAsync(
        string columnName,
        string value,
        string sansJson,
        string hostName,
        CancellationToken cancellationToken)
    {
        using var sansDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sansJson) ? "[]" : sansJson);

        var payload = JsonSerializer.Serialize(new
        {
            sans = sansDoc.RootElement,
            hostname = hostName.Trim().ToLower()
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

        [JsonPropertyName("hostname")]
        public string? HostName { get; set; }

        [JsonPropertyName("point")]
        public int Point { get; set; }

        [JsonPropertyName("open_pts")]
        public int OpenPts { get; set; }

        [JsonPropertyName("open_confirm_gap_pts")]
        public int ConfirmGapPts { get; set; }

        [JsonPropertyName("open_hold_confirm_ms")]
        public int HoldConfirmMs { get; set; }

        [JsonPropertyName("close_pts")]
        public int ClosePts { get; set; }

        [JsonPropertyName("close_confirm_gap_pts")]
        public int CloseConfirmGapPts { get; set; }

        [JsonPropertyName("close_hold_confirm_ms")]
        public int CloseHoldConfirmMs { get; set; }

        [JsonPropertyName("start_time_hold")]
        public int StartTimeHold { get; set; }

        [JsonPropertyName("end_time_hold")]
        public int EndTimeHold { get; set; }

        [JsonPropertyName("start_wait_time")]
        public int StartWaitTime { get; set; }

        [JsonPropertyName("end_wait_time")]
        public int EndWaitTime { get; set; }
    }
}
