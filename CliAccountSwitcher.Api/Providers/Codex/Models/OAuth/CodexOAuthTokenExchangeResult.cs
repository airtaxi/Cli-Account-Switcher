using System;

namespace CliAccountSwitcher.Api.Providers.Codex.Models.OAuth;

public sealed class CodexOAuthTokenExchangeResult
{
    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public string IdentityToken { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string PlanType { get; set; } = "";

    public int ExpiresInSeconds { get; set; } = 3600;

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    public string RawResponseText { get; set; } = "";
}
