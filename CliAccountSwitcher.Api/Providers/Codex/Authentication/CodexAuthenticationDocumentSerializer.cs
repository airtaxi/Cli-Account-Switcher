using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;

namespace CliAccountSwitcher.Api.Providers.Codex.Authentication;

public sealed class CodexAuthenticationDocumentSerializer
{
    private static readonly JsonSerializerOptions s_indentedJsonSerializerOptions = new() { WriteIndented = true };

    public static CodexAuthenticationDocument Parse(string authenticationDocumentText)
    {
        var rootObject = ParseRootObject(authenticationDocumentText);
        return CreateAuthenticationDocument(rootObject);
    }

    public static bool TryParse(string authenticationDocumentText, out CodexAuthenticationDocument? codexAuthenticationDocument)
    {
        try
        {
            codexAuthenticationDocument = Parse(authenticationDocumentText);
            return true;
        }
        catch
        {
            codexAuthenticationDocument = null;
            return false;
        }
    }

    public static string Normalize(string authenticationDocumentText)
    {
        var rootObject = ParseRootObject(authenticationDocumentText);
        var codexAuthenticationDocument = CreateAuthenticationDocument(rootObject);

        rootObject["auth_mode"] = string.IsNullOrWhiteSpace(codexAuthenticationDocument.AuthenticationMode) ? "chatgpt" : codexAuthenticationDocument.AuthenticationMode;
        if (!rootObject.ContainsKey("OPENAI_API_KEY")) rootObject["OPENAI_API_KEY"] = null;

        var tokenObject = rootObject["tokens"] as JsonObject ?? [];
        rootObject["tokens"] = tokenObject;

        SetTokenValue(rootObject, tokenObject, "id_token", codexAuthenticationDocument.GetEffectiveIdentityToken());
        SetTokenValue(rootObject, tokenObject, "access_token", codexAuthenticationDocument.GetEffectiveAccessToken());
        SetTokenValue(rootObject, tokenObject, "refresh_token", codexAuthenticationDocument.GetEffectiveRefreshToken());
        SetTokenValue(rootObject, tokenObject, "account_id", codexAuthenticationDocument.GetEffectiveAccountIdentifier());

        return rootObject.ToJsonString(s_indentedJsonSerializerOptions);
    }

    public string Serialize(CodexAuthenticationDocument codexAuthenticationDocument)
    {
        ArgumentNullException.ThrowIfNull(codexAuthenticationDocument);

        var rootObject = new JsonObject
        {
            ["auth_mode"] = string.IsNullOrWhiteSpace(codexAuthenticationDocument.AuthenticationMode) ? "chatgpt" : codexAuthenticationDocument.AuthenticationMode,
            ["OPENAI_API_KEY"] = codexAuthenticationDocument.OpenAiApiKey is null ? null : JsonValue.Create(codexAuthenticationDocument.OpenAiApiKey),
            ["tokens"] = new JsonObject
            {
                ["id_token"] = codexAuthenticationDocument.GetEffectiveIdentityToken(),
                ["access_token"] = codexAuthenticationDocument.GetEffectiveAccessToken(),
                ["refresh_token"] = codexAuthenticationDocument.GetEffectiveRefreshToken(),
                ["account_id"] = codexAuthenticationDocument.GetEffectiveAccountIdentifier()
            },
            ["id_token"] = codexAuthenticationDocument.GetEffectiveIdentityToken(),
            ["access_token"] = codexAuthenticationDocument.GetEffectiveAccessToken(),
            ["refresh_token"] = codexAuthenticationDocument.GetEffectiveRefreshToken(),
            ["account_id"] = codexAuthenticationDocument.GetEffectiveAccountIdentifier(),
            ["last_refresh"] = codexAuthenticationDocument.LastRefreshText,
            ["email"] = codexAuthenticationDocument.EmailAddress,
            ["type"] = string.IsNullOrWhiteSpace(codexAuthenticationDocument.AuthenticationType) ? "codex" : codexAuthenticationDocument.AuthenticationType,
            ["expired"] = codexAuthenticationDocument.ExpirationText
        };

        return rootObject.ToJsonString(s_indentedJsonSerializerOptions);
    }

    private static CodexAuthenticationDocument CreateAuthenticationDocument(JsonObject rootObject)
    {
        var identityToken = ReadCompatibleString(rootObject, "id_token");
        var identityProfile = string.IsNullOrWhiteSpace(identityToken) ? null : CodexJsonWebTokenParser.TryReadIdentityProfile(identityToken);

        return new CodexAuthenticationDocument
        {
            AuthenticationMode = ReadString(rootObject, "auth_mode") ?? "chatgpt",
            OpenAiApiKey = ReadString(rootObject, "OPENAI_API_KEY"),
            Tokens = new CodexAuthenticationTokenDocument
            {
                IdentityToken = ReadNestedString(rootObject, "tokens", "id_token") ?? "",
                AccessToken = ReadNestedString(rootObject, "tokens", "access_token") ?? "",
                RefreshToken = ReadNestedString(rootObject, "tokens", "refresh_token") ?? "",
                AccountIdentifier = ReadNestedString(rootObject, "tokens", "account_id") ?? ""
            },
            IdentityToken = identityToken,
            AccessToken = ReadCompatibleString(rootObject, "access_token"),
            RefreshToken = ReadCompatibleString(rootObject, "refresh_token"),
            AccountIdentifier = ReadCompatibleString(rootObject, "account_id", identityProfile?.AccountIdentifier ?? ""),
            LastRefreshText = ReadString(rootObject, "last_refresh") ?? "",
            EmailAddress = ReadString(rootObject, "email") ?? identityProfile?.EmailAddress ?? "",
            AuthenticationType = ReadString(rootObject, "type") ?? "codex",
            ExpirationText = ReadString(rootObject, "expired") ?? ""
        };
    }

    private static JsonObject ParseRootObject(string authenticationDocumentText)
    {
        if (string.IsNullOrWhiteSpace(authenticationDocumentText)) throw new InvalidDataException("The authentication document is empty.");

        var rootNode = JsonNode.Parse(authenticationDocumentText);
        if (rootNode is JsonObject rootObject) return rootObject;
        throw new InvalidDataException("The authentication document must be a JSON object.");
    }

    private static void SetTokenValue(JsonObject rootObject, JsonObject tokenObject, string propertyName, string propertyValue)
    {
        if (string.IsNullOrWhiteSpace(propertyValue)) return;
        tokenObject[propertyName] = propertyValue;
        rootObject[propertyName] = propertyValue;
    }

    private static string ReadCompatibleString(JsonObject rootObject, string propertyName, string defaultValue = "")
    {
        var nestedValue = ReadNestedString(rootObject, "tokens", propertyName);
        if (!string.IsNullOrWhiteSpace(nestedValue)) return nestedValue;

        var topLevelValue = ReadString(rootObject, propertyName);
        return string.IsNullOrWhiteSpace(topLevelValue) ? defaultValue : topLevelValue;
    }

    private static string? ReadNestedString(JsonObject rootObject, string objectName, string propertyName)
    {
        if (rootObject[objectName] is not JsonObject nestedObject) return null;
        return ReadString(nestedObject, propertyName);
    }

    private static string? ReadString(JsonObject jsonObject, string propertyName)
    {
        if (!jsonObject.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null) return null;
        return propertyNode.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? propertyNode.ToString() : null;
    }
}
