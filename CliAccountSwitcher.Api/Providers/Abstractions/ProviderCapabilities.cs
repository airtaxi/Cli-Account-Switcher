namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderCapabilities
{
    public bool SupportsAuthenticationDocumentNormalization { get; set; }

    public bool SupportsModels { get; set; }

    public bool SupportsUsage { get; set; }

    public bool SupportsResponses { get; set; }

    public bool SupportsStreamingResponses { get; set; }

    public bool SupportsSavedAccounts { get; set; }

    public bool SupportsStoredAccountUsage { get; set; }

    public bool SupportsInteractiveLogin { get; set; }
}
