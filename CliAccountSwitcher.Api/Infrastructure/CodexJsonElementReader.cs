using System.Text.Json;

namespace CliAccountSwitcher.Api.Infrastructure;

public static class CodexJsonElementReader
{
    public static string? ReadStringOrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        return propertyElement.ValueKind == JsonValueKind.String ? propertyElement.GetString() : propertyElement.ToString();
    }

    public static int? ReadInt32OrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var propertyValue)) return propertyValue;
        return int.TryParse(propertyElement.ToString(), out var parsedValue) ? parsedValue : null;
    }

    public static long? ReadInt64OrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt64(out var propertyValue)) return propertyValue;
        return long.TryParse(propertyElement.ToString(), out var parsedValue) ? parsedValue : null;
    }

    public static bool TryGetProperty(JsonElement jsonElement, string propertyName, out JsonElement propertyElement)
    {
        foreach (var candidateProperty in jsonElement.EnumerateObject())
        {
            if (!string.Equals(candidateProperty.Name, propertyName, System.StringComparison.OrdinalIgnoreCase)) continue;
            propertyElement = candidateProperty.Value;
            return true;
        }

        propertyElement = default;
        return false;
    }
}
