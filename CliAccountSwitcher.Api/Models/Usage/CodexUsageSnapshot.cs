namespace CliAccountSwitcher.Api.Models.Usage;

public sealed class CodexUsageSnapshot
{
    public string PlanType { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public int HttpStatusCode { get; set; }

    public string RawResponseText { get; set; } = "";

    public CodexUsageWindow PrimaryWindow { get; set; } = new();

    public CodexUsageWindow SecondaryWindow { get; set; } = new();
}
