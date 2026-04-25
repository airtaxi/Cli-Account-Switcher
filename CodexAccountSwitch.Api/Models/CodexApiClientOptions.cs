using System;
using System.IO;

namespace CodexAccountSwitch.Api.Models;

public sealed class CodexApiClientOptions
{
    public string CodexHomeDirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public string DefaultCodexVersion { get; set; } = "0.98.0";

    public string DefaultVisualStudioCodeExtensionVersion { get; set; } = "0.4.71";

    public string OAuthClientId { get; set; } = "app_EMoamEEZ73f0CkXaXp7hrann";

    public bool UseDynamicOAuthRedirectPort { get; set; } = true;

    public int OAuthRedirectPort { get; set; } = 1455;

    public int MinimumOAuthRedirectPort { get; set; } = 49152;

    public int MaximumOAuthRedirectPort { get; set; } = 65535;

    public string OAuthRedirectPath { get; set; } = "/auth/callback";

    public TimeSpan OAuthTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
