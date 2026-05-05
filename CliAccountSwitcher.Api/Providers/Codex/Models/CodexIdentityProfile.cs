namespace CliAccountSwitcher.Api.Providers.Codex.Models;

public sealed class CodexIdentityProfile
{
    public string EmailAddress { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string PlanType { get; set; } = "";
}
