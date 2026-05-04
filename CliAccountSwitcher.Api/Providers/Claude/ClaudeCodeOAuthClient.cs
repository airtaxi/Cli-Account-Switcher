using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliAccountSwitcher.Api.Infrastructure.Http;
using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Providers.Claude;

internal sealed class ClaudeCodeOAuthClient(HttpClient httpClient)
{
    private const string ClientIdentifier = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly Uri s_tokenAddress = new("https://platform.claude.com/v1/oauth/token");

    public async Task<ClaudeCodeTokenRefreshResult> RefreshTokenAsync(ClaudeCodeCredentialDocument credentialDocument, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialDocument.RefreshToken)) throw new ProviderAuthenticationExpiredException("Claude Code login is required because the refresh token is missing.");

        var requestBody = new JsonObject
        {
            ["grant_type"] = JsonValue.Create("refresh_token"),
            ["refresh_token"] = JsonValue.Create(credentialDocument.RefreshToken),
            ["client_id"] = JsonValue.Create(ClientIdentifier),
            ["scope"] = JsonValue.Create(ClaudeCodeOAuthScopes.DefaultScopeText)
        };

        var requestBodyJson = requestBody.ToJsonString(ProviderJsonSerializerOptions.Default);
        using var stringContent = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, s_tokenAddress) { Content = stringContent };

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await CodexHttpResponseValidator.ReadRequiredContentAsync(httpResponseMessage, cancellationToken);
        if (httpResponseMessage.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ProviderAuthenticationExpiredException($"Claude Code token refresh failed. Login again. Response: {responseText}");
        if (!httpResponseMessage.IsSuccessStatusCode) throw new ProviderActionRequiredException($"Claude Code token refresh failed. Login again. Response: {responseText}");

        return ParseTokenRefreshResult(responseText, credentialDocument.ExpiresAt);
    }

    private static ClaudeCodeTokenRefreshResult ParseTokenRefreshResult(string responseText, long previousExpiresAt)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var rootElement = jsonDocument.RootElement;
        var accessToken = ReadStringOrNull(rootElement, "access_token") ?? "";
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ProviderAuthenticationExpiredException("Claude Code token refresh did not return an access token. Login again.");

        var refreshToken = ReadStringOrNull(rootElement, "refresh_token") ?? "";
        var scopes = ParseScopes(ReadStringOrNull(rootElement, "scope"));
        var expiresInSeconds = ReadInt32OrNull(rootElement, "expires_in") ?? 3600;
        var issuedAt = DateTimeOffset.UtcNow;

        return new ClaudeCodeTokenRefreshResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Scopes = scopes,
            ExpiresInSeconds = expiresInSeconds,
            ExpiresAt = ClaudeCodeCredentialDocument.CalculateExpiresAt(issuedAt, expiresInSeconds, previousExpiresAt),
            RawResponseText = responseText
        };
    }

    private static string? ReadStringOrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        return propertyElement.ValueKind == JsonValueKind.String ? propertyElement.GetString() : propertyElement.ToString();
    }

    private static int? ReadInt32OrNull(JsonElement jsonElement, string propertyName)
    {
        if (!TryGetProperty(jsonElement, propertyName, out var propertyElement)) return null;
        if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var propertyValue)) return propertyValue;
        return int.TryParse(propertyElement.ToString(), out var parsedValue) ? parsedValue : null;
    }

    private static IReadOnlyList<string> ParseScopes(string? scopeText)
        => string.IsNullOrWhiteSpace(scopeText)
            ? []
            : scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
}
