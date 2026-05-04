namespace CliAccountSwitcher.Api.Providers.Claude;

internal sealed class ClaudeCodeStoredAccountPayload
{
    public string CredentialsJson { get; set; } = "";

    public string GlobalConfigJson { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string OrganizationIdentifier { get; set; } = "";

    public string OrganizationName { get; set; } = "";

    public string PlanType { get; set; } = "";
}
