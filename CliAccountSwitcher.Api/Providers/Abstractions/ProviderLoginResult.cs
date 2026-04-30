namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderLoginResult
{
    public CliProviderKind ProviderKind { get; set; }

    public string OutputText { get; set; } = "";

    public string CompletionMessage { get; set; } = "";

    public bool IsAuthenticationDocument { get; set; }

    public bool ShouldPromptSaveCurrentAccount { get; set; }
}
