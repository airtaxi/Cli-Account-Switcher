using System.Text.Json;
using System.Text.Json.Nodes;
using CliAccountSwitcher.Api.Providers;

namespace CliAccountSwitcher.Api.Providers.Claude;

public sealed class ClaudeCodeCredentialDocument
{
    private const long MillisecondUnixTimestampThreshold = 10_000_000_000;

    public string RawJson { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public long ExpiresAt { get; set; } = -1;

    public static ClaudeCodeCredentialDocument Parse(string credentialsJson)
    {
        var rootObject = ParseRootObject(credentialsJson);
        var oauthObject = rootObject["claudeAiOauth"] as JsonObject;

        return new ClaudeCodeCredentialDocument
        {
            RawJson = credentialsJson,
            AccessToken = ReadString(oauthObject, "accessToken") ?? "",
            RefreshToken = ReadString(oauthObject, "refreshToken") ?? "",
            ExpiresAt = ReadInt64(oauthObject, "expiresAt") ?? -1
        };
    }

    public bool IsAccessTokenExpiringSoon(DateTimeOffset currentTime)
    {
        var expirationTime = ConvertExpiresAtToDateTimeOffset(ExpiresAt);
        return expirationTime is not null && expirationTime <= currentTime.AddMinutes(5);
    }

    public string GetExpirationText()
    {
        var expirationTime = ConvertExpiresAtToDateTimeOffset(ExpiresAt);
        return expirationTime?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? ExpiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal string CreateUpdatedCredentialsJson(ClaudeCodeTokenRefreshResult tokenRefreshResult)
    {
        var rootObject = ParseRootObject(RawJson);
        var oauthObject = rootObject["claudeAiOauth"] as JsonObject ?? [];
        rootObject["claudeAiOauth"] = oauthObject;

        oauthObject["accessToken"] = tokenRefreshResult.AccessToken;
        if (!string.IsNullOrWhiteSpace(tokenRefreshResult.RefreshToken)) oauthObject["refreshToken"] = tokenRefreshResult.RefreshToken;
        oauthObject["expiresAt"] = tokenRefreshResult.ExpiresAt;

        return rootObject.ToJsonString(ProviderJsonSerializerOptions.Default);
    }

    public static long CalculateExpiresAt(DateTimeOffset issuedAt, int expiresInSeconds, long previousExpiresAt)
    {
        var expirationTime = issuedAt.AddSeconds(expiresInSeconds);
        return previousExpiresAt is > 0 and < MillisecondUnixTimestampThreshold
            ? expirationTime.ToUnixTimeSeconds()
            : expirationTime.ToUnixTimeMilliseconds();
    }

    public static DateTimeOffset? ConvertExpiresAtToDateTimeOffset(long expiresAt)
    {
        if (expiresAt <= 0) return null;

        try
        {
            return expiresAt >= MillisecondUnixTimestampThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAt)
                : DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        }
        catch { return null; }
    }

    private static JsonObject ParseRootObject(string credentialsJson)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson)) throw new InvalidDataException("The Claude Code credentials document is empty.");

        var rootNode = JsonNode.Parse(credentialsJson);
        if (rootNode is JsonObject rootObject) return rootObject;
        throw new InvalidDataException("The Claude Code credentials document must be a JSON object.");
    }

    private static string? ReadString(JsonObject? jsonObject, string propertyName)
    {
        if (jsonObject is null || !jsonObject.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null) return null;
        return propertyNode.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? propertyNode.ToString() : null;
    }

    private static long? ReadInt64(JsonObject? jsonObject, string propertyName)
    {
        var propertyText = ReadString(jsonObject, propertyName);
        return long.TryParse(propertyText, out var propertyValue) ? propertyValue : null;
    }
}
