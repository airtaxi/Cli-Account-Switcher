namespace CliAccountSwitcher.Api.Providers.ClaudeCode.Authentication;

internal static class ClaudeCodeOAuthScopes
{
    public const string Profile = "user:profile";
    public const string Inference = "user:inference";

    public static IReadOnlyList<string> DefaultScopes { get; } =
    [
        Profile,
        Inference,
        "user:sessions:claude_code",
        "user:mcp_servers",
        "user:file_upload"
    ];

    public static string DefaultScopeText { get; } = string.Join(' ', DefaultScopes);
}
