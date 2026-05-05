using CliAccountSwitcher.Api.Providers.ClaudeCode.Authentication;

namespace CliAccountSwitcher.Api.Providers.ClaudeCode.Models;

public sealed class ClaudeCodeAccountState
{
    public string CredentialsJson { get; set; } = "";

    public string GlobalConfigJson { get; set; } = "";

    public ClaudeCodeCredentialDocument CredentialDocument { get; set; } = new();

    public ClaudeCodeGlobalConfigDocument GlobalConfigDocument { get; set; } = new();
}
