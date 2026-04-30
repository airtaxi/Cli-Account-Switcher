using System.Text.Json;
using System.Text.Json.Nodes;

namespace CliAccountSwitcher.Api.Providers.Claude;

public sealed class ClaudeCodeGlobalConfigDocument
{
    public string RawJson { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string OrganizationIdentifier { get; set; } = "";

    public string OrganizationName { get; set; } = "";

    public static ClaudeCodeGlobalConfigDocument Parse(string globalConfigJson)
    {
        var rootObject = ParseRootObject(globalConfigJson);
        var oauthAccountObject = rootObject["oauthAccount"] as JsonObject;

        return new ClaudeCodeGlobalConfigDocument
        {
            RawJson = globalConfigJson,
            EmailAddress = ReadString(oauthAccountObject, "emailAddress") ?? "",
            DisplayName = ReadString(oauthAccountObject, "displayName") ?? "",
            AccountIdentifier = ReadString(oauthAccountObject, "accountUuid") ?? "",
            OrganizationIdentifier = ReadString(oauthAccountObject, "organizationUuid") ?? "",
            OrganizationName = ReadString(oauthAccountObject, "organizationName") ?? ""
        };
    }

    private static JsonObject ParseRootObject(string globalConfigJson)
    {
        if (string.IsNullOrWhiteSpace(globalConfigJson)) throw new InvalidDataException("The Claude Code global config document is empty.");

        var rootNode = JsonNode.Parse(globalConfigJson);
        if (rootNode is JsonObject rootObject) return rootObject;
        throw new InvalidDataException("The Claude Code global config document must be a JSON object.");
    }

    private static string? ReadString(JsonObject? jsonObject, string propertyName)
    {
        if (jsonObject is null || !jsonObject.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null) return null;
        return propertyNode.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? propertyNode.ToString() : null;
    }
}
