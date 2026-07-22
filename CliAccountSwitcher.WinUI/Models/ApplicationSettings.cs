using CliAccountSwitcher.Api.Providers.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System.Text.Json.Serialization;

namespace CliAccountSwitcher.WinUI.Models;

public sealed partial class ApplicationSettings : ObservableObject
{
    public const int CurrentSchemaVersion = 2;

    public const int DefaultActiveAccountUsageRefreshIntervalSeconds = 120;

    public const int DefaultInactiveAccountUsageRefreshIntervalSeconds = 900;

    public const int DefaultPrimaryUsageWarningThresholdPercentage = 15;

    public const int DefaultSecondaryUsageWarningThresholdPercentage = 15;

    public const int DefaultPrimaryUsageSurgeNotificationThresholdPercentage = 10;

    public const int DefaultPrimaryUsageSurgeNotificationWindowMinutes = 5;

    // Must match the default value of Deskband11Lib.Core.TaskbarContentHostOptions.ManualSlotPriority.
    public const ushort DefaultManualSlotPriority = 65535;

    [ObservableProperty]
    public partial int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [ObservableProperty]
    public partial ElementTheme Theme { get; set; } = ElementTheme.Default;

    [ObservableProperty]
    public partial string LanguageOverride { get; set; } = "";

    [ObservableProperty]
    [JsonConverter(typeof(JsonStringEnumConverter<CliProviderKind>))]
    public partial CliProviderKind SelectedProviderKind { get; set; } = CliProviderKind.Codex;

    [ObservableProperty]
    public partial bool IsAutomaticUpdateCheckEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsStartupLaunchEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool HideTaskbarUsage { get; set; }

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

    [ObservableProperty]
    public partial bool IsPrimaryUsageSurgeNotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int PrimaryUsageSurgeNotificationThresholdPercentage { get; set; } = DefaultPrimaryUsageSurgeNotificationThresholdPercentage;

    [ObservableProperty]
    public partial int PrimaryUsageSurgeNotificationWindowMinutes { get; set; } = DefaultPrimaryUsageSurgeNotificationWindowMinutes;

    [ObservableProperty]
    public partial int PreferredMonitorIdentity { get; set; }

    [ObservableProperty]
    public partial ushort ManualSlotPriority { get; set; } = DefaultManualSlotPriority;
}
