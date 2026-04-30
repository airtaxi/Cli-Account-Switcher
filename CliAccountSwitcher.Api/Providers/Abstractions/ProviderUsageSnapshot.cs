namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderUsageSnapshot
{
    public CliProviderKind ProviderKind { get; set; }

    public string PlanType { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string RawResponseText { get; set; } = "";

    public ProviderUsageWindow FiveHour { get; set; } = new();

    public ProviderUsageWindow SevenDay { get; set; } = new();
}
