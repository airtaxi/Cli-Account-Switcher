using CliAccountSwitcher.Api.Providers.Zai.Models.Usage;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class ZaiAccount
{
    public string ApiKey { get; set; } = "";

    public bool PreferChinaEndpoint { get; set; }

    public bool IsActive { get; set; }

    public string CustomAlias { get; set; } = "";

    public ZaiUsageSnapshot LastZaiUsageSnapshot { get; set; } = new();

    public bool IsTokenExpired { get; set; }

    public DateTimeOffset? LastUsageRefreshTime { get; set; }

    [JsonIgnore]
    public string AccountIdentifier => ComputeAccountIdentifier(ApiKey);

    [JsonIgnore]
    public string PlanType => LastZaiUsageSnapshot?.PlanLevel ?? "";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(CustomAlias) ? BuildDefaultDisplayName(PlanType) : CustomAlias;

    public void MarkAsExpired() => IsTokenExpired = true;

    public void MarkAsValid() => IsTokenExpired = false;

    public static string ComputeAccountIdentifier(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(hashBytes)[..32];
    }

    private static string BuildDefaultDisplayName(string planLevel)
    {
        if (string.IsNullOrWhiteSpace(planLevel)) return "Z.ai";
        return string.Format(CultureInfo.CurrentCulture, "Z.ai ({0})", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(planLevel.ToLowerInvariant()));
    }
}
