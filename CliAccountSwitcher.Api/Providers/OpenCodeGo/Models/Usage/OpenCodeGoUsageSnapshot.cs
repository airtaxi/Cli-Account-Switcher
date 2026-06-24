namespace CliAccountSwitcher.Api.Providers.OpenCodeGo.Models.Usage;

public sealed class OpenCodeGoUsageSnapshot
{
    public string PlanLevel { get; set; } = "";

    public string RawResponseText { get; set; } = "";

    public OpenCodeGoUsageWindow RollingUsage { get; set; } = new();

    public OpenCodeGoUsageWindow WeeklyUsage { get; set; } = new();

    public OpenCodeGoUsageWindow MonthlyUsage { get; set; } = new();
}
