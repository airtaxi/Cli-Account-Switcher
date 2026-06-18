namespace CliAccountSwitcher.Api.Providers.Zai;

public static class ZaiApiConventions
{
    public static Uri GlobalBaseUri { get; } = new("https://api.z.ai");

    public static Uri ChinaBaseUri { get; } = new("https://open.bigmodel.cn");

    public static string QuotaLimitPath => "/api/monitor/usage/quota/limit";
}
