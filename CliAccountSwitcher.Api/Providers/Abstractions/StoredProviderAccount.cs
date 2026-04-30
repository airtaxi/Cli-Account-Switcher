namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class StoredProviderAccount
{
    public CliProviderKind ProviderKind { get; set; }

    public string StoredAccountIdentifier { get; set; } = "";

    public int SlotNumber { get; set; }

    public string EmailAddress { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";

    public string OrganizationIdentifier { get; set; } = "";

    public string OrganizationName { get; set; } = "";

    public bool IsActive { get; set; }

    public bool IsTokenExpired { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}
