namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderResponseRequest
{
    public string Model { get; set; } = "";

    public string Text { get; set; } = "";

    public string? Instructions { get; set; }
}
