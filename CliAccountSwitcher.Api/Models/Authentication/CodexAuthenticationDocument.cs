using CliAccountSwitcher.Api.Authentication;
using CliAccountSwitcher.Api.Models;

namespace CliAccountSwitcher.Api.Models.Authentication;

public sealed class CodexAuthenticationDocument
{
    public string AuthenticationMode { get; set; } = "chatgpt";

    public string? OpenAiApiKey { get; set; }

    public CodexAuthenticationTokenDocument Tokens { get; set; } = new();

    public string IdentityToken { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string LastRefreshText { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string AuthenticationType { get; set; } = "codex";

    public string ExpirationText { get; set; } = "";

    public string GetEffectiveIdentityToken() => string.IsNullOrWhiteSpace(Tokens.IdentityToken) ? IdentityToken : Tokens.IdentityToken;

    public string GetEffectiveAccessToken() => string.IsNullOrWhiteSpace(Tokens.AccessToken) ? AccessToken : Tokens.AccessToken;

    public string GetEffectiveRefreshToken() => string.IsNullOrWhiteSpace(Tokens.RefreshToken) ? RefreshToken : Tokens.RefreshToken;

    public string GetEffectiveAccountIdentifier() => string.IsNullOrWhiteSpace(Tokens.AccountIdentifier) ? AccountIdentifier : Tokens.AccountIdentifier;

    public CodexRequestContext CreateRequestContext()
    {
        var effectiveAccessToken = GetEffectiveAccessToken();
        if (string.IsNullOrWhiteSpace(effectiveAccessToken)) throw new CodexApiException("The authentication document does not contain an access token.");

        var effectiveAccountIdentifier = GetEffectiveAccountIdentifier();
        if (string.IsNullOrWhiteSpace(effectiveAccountIdentifier))
        {
            var identityProfile = TryReadIdentityProfile();
            effectiveAccountIdentifier = identityProfile?.AccountIdentifier ?? "";
        }

        if (string.IsNullOrWhiteSpace(effectiveAccountIdentifier)) throw new CodexApiException("The authentication document does not contain an account identifier.");

        return new CodexRequestContext
        {
            AccessToken = effectiveAccessToken,
            AccountId = effectiveAccountIdentifier
        };
    }

    public CodexIdentityProfile? TryReadIdentityProfile()
    {
        var identityToken = GetEffectiveIdentityToken();
        return string.IsNullOrWhiteSpace(identityToken) ? null : CodexJsonWebTokenParser.TryReadIdentityProfile(identityToken);
    }
}
