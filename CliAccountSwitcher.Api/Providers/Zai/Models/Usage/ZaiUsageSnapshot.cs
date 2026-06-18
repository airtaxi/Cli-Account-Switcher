namespace CliAccountSwitcher.Api.Providers.Zai.Models.Usage;

public sealed class ZaiUsageSnapshot
{
    public string PlanLevel { get; set; } = "";

    public string RawResponseText { get; set; } = "";

    public int HttpStatusCode { get; set; }

    public bool UsedChinaEndpoint { get; set; }

    public ZaiUsageWindow FiveHour { get; set; } = new();

    public ZaiUsageWindow SevenDay { get; set; } = new();
}
