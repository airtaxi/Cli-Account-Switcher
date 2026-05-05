namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderAccountDocumentSet
{
    public string AuthenticationDocumentText { get; set; } = "";

    public string CredentialsDocumentText { get; set; } = "";

    public string GlobalConfigDocumentText { get; set; } = "";
}
