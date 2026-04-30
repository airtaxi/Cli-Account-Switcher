namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderIdentityProfile
{
    public CliProviderKind ProviderKind { get; set; }

    public string EmailAddress { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string OrganizationIdentifier { get; set; } = "";

    public string OrganizationName { get; set; } = "";

    public string PlanType { get; set; } = "";

    public string AccessTokenPreview { get; set; } = "";

    public string ExpirationText { get; set; } = "";

    public bool IsLoggedIn { get; set; }
}
