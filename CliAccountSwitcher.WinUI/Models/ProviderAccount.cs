using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class ProviderAccount
{
    public CliProviderKind ProviderKind { get; set; }

    public string AccountIdentifier { get; set; } = "";

    public string ProviderAccountIdentifier { get; set; } = "";

    public string AccountDetailText { get; set; } = "";

    public string CustomAlias { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public string PlanType { get; set; } = "";

    public bool IsActive { get; set; }

    public bool IsTokenExpired { get; set; }

    public ProviderUsageSnapshot LastProviderUsageSnapshot { get; set; } = new();

    public DateTimeOffset? LastUsageRefreshTime { get; set; }
}
