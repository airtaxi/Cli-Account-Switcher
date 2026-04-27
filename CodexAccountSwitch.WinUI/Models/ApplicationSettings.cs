using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace CodexAccountSwitch.WinUI.Models;

public sealed partial class ApplicationSettings : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public const int DefaultActiveAccountUsageRefreshIntervalSeconds = 120;

    public const int DefaultInactiveAccountUsageRefreshIntervalSeconds = 900;

    public const int DefaultPrimaryUsageWarningThresholdPercentage = 15;

    public const int DefaultSecondaryUsageWarningThresholdPercentage = 15;

    [ObservableProperty]
    public partial int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [ObservableProperty]
    public partial ElementTheme Theme { get; set; } = ElementTheme.Default;

    [ObservableProperty]
    public partial string LanguageOverride { get; set; } = "";

    [ObservableProperty]
    public partial bool IsAutomaticUpdateCheckEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsStartupLaunchEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpiredAccountAutomaticDeletionEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsExpiredAccountNotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsActiveAccountUsageAutomaticRefreshEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int ActiveAccountUsageRefreshIntervalSeconds { get; set; } = DefaultActiveAccountUsageRefreshIntervalSeconds;

    [ObservableProperty]
    public partial bool IsInactiveAccountUsageAutomaticRefreshEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int InactiveAccountUsageRefreshIntervalSeconds { get; set; } = DefaultInactiveAccountUsageRefreshIntervalSeconds;

    [ObservableProperty]
    public partial int PrimaryUsageWarningThresholdPercentage { get; set; } = DefaultPrimaryUsageWarningThresholdPercentage;

    [ObservableProperty]
    public partial int SecondaryUsageWarningThresholdPercentage { get; set; } = DefaultSecondaryUsageWarningThresholdPercentage;

    [ObservableProperty]
    public partial bool IsPrimaryUsageLowQuotaNotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSecondaryUsageLowQuotaNotificationEnabled { get; set; } = true;
}
