using System.Text.Json;

namespace TradeDesktop.Application.Helpers;

public static class SansJsonHelper
{
    public static bool TryParseSans(string? sansJson, out string mapName1, out string mapName2)
    {
        mapName1 = string.Empty;
        mapName2 = string.Empty;

        if (string.IsNullOrWhiteSpace(sansJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(sansJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            mapName1 = doc.RootElement.GetArrayLength() > 0
                ? (doc.RootElement[0].GetString() ?? string.Empty).Trim()
                : string.Empty;

            mapName2 = doc.RootElement.GetArrayLength() > 1
                ? (doc.RootElement[1].GetString() ?? string.Empty).Trim()
                : string.Empty;

            return !string.IsNullOrWhiteSpace(mapName1) || !string.IsNullOrWhiteSpace(mapName2);
        }
        catch
        {
            mapName1 = string.Empty;
            mapName2 = string.Empty;
            return false;
        }
    }

    public static string BuildSans(string? mapName1, string? mapName2)
    {
        var sans = new[]
        {
            mapName1?.Trim() ?? string.Empty,
            mapName2?.Trim() ?? string.Empty
        };

        return JsonSerializer.Serialize(sans);
    }
}
