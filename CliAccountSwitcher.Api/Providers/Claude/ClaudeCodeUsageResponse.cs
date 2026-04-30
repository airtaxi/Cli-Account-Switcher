using System.Globalization;
using System.Text.Json;
using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Providers.Claude;

internal sealed class ClaudeCodeUsageResponse
{
    public static ProviderUsageSnapshot Parse(string responseText, string emailAddress)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var rootElement = jsonDocument.RootElement;

        return new ProviderUsageSnapshot
        {
            ProviderKind = CliProviderKind.ClaudeCode,
            PlanType = "",
            EmailAddress = emailAddress,
            RawResponseText = responseText,
            FiveHour = CreateUsageWindow(rootElement, "five_hour"),
            SevenDay = CreateUsageWindow(rootElement, "seven_day")
        };
    }

    private static ProviderUsageWindow CreateUsageWindow(JsonElement rootElement, string propertyName)
    {
        if (!TryGetProperty(rootElement, propertyName, out var windowElement) || windowElement.ValueKind != JsonValueKind.Object) return new ProviderUsageWindow();

        var usedPercentage = ReadInt32OrNull(windowElement, "utilization") ?? -1;
        var resetAt = ReadDateTimeOffsetOrNull(windowElement, "resets_at");

        return new ProviderUsageWindow
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

    private static int? ReadInt32OrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var int32Value)) return int32Value;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetDouble(out var doubleValue)) return Convert.ToInt32(Math.Round(doubleValue));
        return int.TryParse(propertyElement.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) ? parsedValue : null;
    }

    private static DateTimeOffset? ReadDateTimeOffsetOrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        return DateTimeOffset.TryParse(propertyElement.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedValue) ? parsedValue.ToUniversalTime() : null;
    }
}
