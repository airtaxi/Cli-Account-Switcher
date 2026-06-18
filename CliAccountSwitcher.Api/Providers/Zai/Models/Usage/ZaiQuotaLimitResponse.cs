using System.Text.Json;

namespace CliAccountSwitcher.Api.Providers.Zai.Models.Usage;

internal static class ZaiQuotaLimitResponse
{
    public static ZaiUsageSnapshot Parse(string responseText, int httpStatusCode, bool usedChinaEndpoint = false)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var rootElement = jsonDocument.RootElement;
        var dataElement = TryGetProperty(rootElement, "data", out var data) && data.ValueKind == JsonValueKind.Object ? data : rootElement;

        var snapshot = new ZaiUsageSnapshot
        {
            PlanLevel = ReadStringOrNull(dataElement, "level") ?? "",
            RawResponseText = responseText,
            HttpStatusCode = httpStatusCode,
            UsedChinaEndpoint = usedChinaEndpoint
        };

        if (TryGetProperty(dataElement, "limits", out var limitsElement) && limitsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var limitElement in limitsElement.EnumerateArray())
            {
                if (limitElement.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(ReadStringOrNull(limitElement, "type"), "TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase)) continue;

                var unit = ReadInt32OrNull(limitElement, "unit") ?? -1;
                var window = CreateUsageWindow(limitElement);
                if (unit == 3) snapshot.FiveHour = window;
                else if (unit == 6) snapshot.SevenDay = window;
            }
        }

        return snapshot;
    }

    private static ZaiUsageWindow CreateUsageWindow(JsonElement limitElement)
    {
        var usedPercentage = ReadInt32OrNull(limitElement, "percentage") ?? -1;
        var resetAt = ReadUnixMillisecondsOrNull(limitElement, "nextResetTime");

        return new ZaiUsageWindow
        {
            UsedPercentage = usedPercentage,
            RemainingPercentage = usedPercentage is < 0 or > 100 ? -1 : 100 - usedPercentage,
            ResetAt = resetAt,
            ResetAfterSeconds = resetAt is null ? -1 : Convert.ToInt64(Math.Round((resetAt.Value - DateTimeOffset.UtcNow).TotalSeconds))
        };
    }

    private static bool TryGetProperty(JsonElement jsonElement, string propertyName, out JsonElement propertyElement)
    {
        foreach (var candidateProperty in jsonElement.EnumerateObject())
        {
            if (!string.Equals(candidateProperty.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            propertyElement = candidateProperty.Value;
            return true;
        }

        propertyElement = default;
        return false;
    }

    private static string? ReadStringOrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        return propertyElement.ValueKind == JsonValueKind.String ? propertyElement.GetString() : null;
    }

    private static int? ReadInt32OrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var int32Value)) return int32Value;
        return int.TryParse(propertyElement.ToString(), out var parsedValue) ? parsedValue : null;
    }

    private static DateTimeOffset? ReadUnixMillisecondsOrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind != JsonValueKind.Number || !propertyElement.TryGetInt64(out var unixMilliseconds)) return null;
        if (unixMilliseconds <= 0) return null;

        try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds); }
        catch { return null; }
    }
}
