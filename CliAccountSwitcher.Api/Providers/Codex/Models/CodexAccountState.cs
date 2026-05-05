using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;

namespace CliAccountSwitcher.Api.Providers.Codex.Models;

public sealed class CodexAccountState
{
    public string AuthenticationDocumentJson { get; set; } = "";

    public CodexAuthenticationDocument AuthenticationDocument { get; set; } = new();
}
