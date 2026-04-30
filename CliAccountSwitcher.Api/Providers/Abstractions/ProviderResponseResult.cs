namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderResponseResult
{
    public CliProviderKind ProviderKind { get; set; }

    public string OutputText { get; set; } = "";

    public string RawResponseText { get; set; } = "";
}
