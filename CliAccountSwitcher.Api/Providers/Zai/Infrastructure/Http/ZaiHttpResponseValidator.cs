using System.Net;
using System.Text.Json;
using CliAccountSwitcher.Api.Providers.Zai.Models;

namespace CliAccountSwitcher.Api.Providers.Zai.Infrastructure.Http;

public static class ZaiHttpResponseValidator
{
    public static async Task<string> EnsureSuccessAndReadContentAsync(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        var responseBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        if (!httpResponseMessage.IsSuccessStatusCode) throw new ZaiApiException($"The Z.ai request failed. HTTP {(int)httpResponseMessage.StatusCode} {httpResponseMessage.StatusCode}.", httpResponseMessage.StatusCode, null, responseBody);

        // Z.ai returns HTTP 200 even for application-level failures (for example an invalid API key produces code 401), so the body code must be inspected.
        var applicationCode = (int?)null;
        var applicationMessage = (string?)null;
        try
        {
            using var jsonDocument = JsonDocument.Parse(responseBody);
            var rootElement = jsonDocument.RootElement;
            if (TryReadInt32(rootElement, "code", out var code)) applicationCode = code;
            if (TryReadString(rootElement, "msg", out var message)) applicationMessage = message;
        }
        catch (JsonException) { }

        if (applicationCode is not null && applicationCode != 200) throw new ZaiApiException(applicationMessage ?? $"The Z.ai request failed. Application code {applicationCode}.", httpResponseMessage.StatusCode, applicationCode, responseBody);

        return responseBody;
    }

    private static bool TryReadInt32(JsonElement jsonElement, string propertyName, out int value)
    {
        value = 0;
        foreach (var candidateProperty in jsonElement.EnumerateObject())
        {
            if (!string.Equals(candidateProperty.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            var propertyElement = candidateProperty.Value;
            if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var int32Value))
            {
                value = int32Value;
                return true;
            }

            return int.TryParse(propertyElement.ToString(), out value);
        }

        return false;
    }

    private static bool TryReadString(JsonElement jsonElement, string propertyName, out string? value)
    {
        value = null;
        foreach (var candidateProperty in jsonElement.EnumerateObject())
        {
            if (!string.Equals(candidateProperty.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            value = candidateProperty.Value.ValueKind == JsonValueKind.String ? candidateProperty.Value.GetString() : candidateProperty.Value.ToString();
            return true;
        }

        return false;
    }
}
