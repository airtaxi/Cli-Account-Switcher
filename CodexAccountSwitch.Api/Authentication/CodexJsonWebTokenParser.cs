using System;
using System.Text;
using System.Text.Json;
using CodexAccountSwitch.Api.Infrastructure;
using CodexAccountSwitch.Api.Models;

namespace CodexAccountSwitch.Api.Authentication;

public static class CodexJsonWebTokenParser
{
    public static CodexIdentityProfile? TryReadIdentityProfile(string identityToken)
    {
        try
        {
            var payloadText = ReadPayloadText(identityToken);
            if (string.IsNullOrWhiteSpace(payloadText)) return null;

            using var jsonDocument = JsonDocument.Parse(payloadText);
            var rootElement = jsonDocument.RootElement;
            var identityProfile = new CodexIdentityProfile
            {
                EmailAddress = CodexJsonElementReader.ReadStringOrNull(rootElement, "email") ?? ""
            };

            if (CodexJsonElementReader.TryGetProperty(rootElement, "https://api.openai.com/auth", out var authenticationElement) && authenticationElement.ValueKind == JsonValueKind.Object)
            {
                identityProfile.AccountIdentifier = CodexJsonElementReader.ReadStringOrNull(authenticationElement, "chatgpt_account_id") ?? "";
                identityProfile.PlanType = CodexJsonElementReader.ReadStringOrNull(authenticationElement, "chatgpt_plan_type") ?? "";
            }

            if (string.IsNullOrWhiteSpace(identityProfile.AccountIdentifier)) identityProfile.AccountIdentifier = CodexJsonElementReader.ReadStringOrNull(rootElement, "chatgpt_account_id") ?? "";
            if (string.IsNullOrWhiteSpace(identityProfile.PlanType)) identityProfile.PlanType = CodexJsonElementReader.ReadStringOrNull(rootElement, "chatgpt_plan_type") ?? "";

            return string.IsNullOrWhiteSpace(identityProfile.EmailAddress) && string.IsNullOrWhiteSpace(identityProfile.AccountIdentifier) && string.IsNullOrWhiteSpace(identityProfile.PlanType) ? null : identityProfile;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadPayloadText(string identityToken)
    {
        var tokenParts = identityToken.Split('.');
        if (tokenParts.Length < 2) return "";

        var payloadText = tokenParts[1].Replace('-', '+').Replace('_', '/');
        payloadText = payloadText.PadRight(payloadText.Length + (4 - payloadText.Length % 4) % 4, '=');
        var payloadBytes = Convert.FromBase64String(payloadText);
        return Encoding.UTF8.GetString(payloadBytes);
    }
}
