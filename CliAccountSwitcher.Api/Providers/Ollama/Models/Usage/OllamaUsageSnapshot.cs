namespace CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;

public sealed class OllamaUsageSnapshot
{
    public string PlanLevel { get; set; } = "";

    public string UserName { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string RawResponseText { get; set; } = "";

    public OllamaUsageWindow SessionUsage { get; set; } = new();

    public OllamaUsageWindow WeeklyUsage { get; set; } = new();
}