namespace CliAccountSwitcher.Api.Providers.Claude;

internal sealed class ClaudeCodeTokenRefreshResult
{
    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public IReadOnlyList<string> Scopes { get; set; } = [];

    public int ExpiresInSeconds { get; set; }

    public long ExpiresAt { get; set; }

    public string RawResponseText { get; set; } = "";
}
