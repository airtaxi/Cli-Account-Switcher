using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace CodexAccountSwitch.WinUI.Models;

public sealed partial class ApplicationSettings : ObservableObject
{
    public const int DefaultPrimaryUsageWarningThresholdPercentage = 15;

    public const int DefaultSecondaryUsageWarningThresholdPercentage = 15;

    [ObservableProperty]
    public partial ElementTheme Theme { get; set; } = ElementTheme.Default;

    [ObservableProperty]
    public partial string LanguageOverride { get; set; } = "";

    [ObservableProperty]
    public partial int PrimaryUsageWarningThresholdPercentage { get; set; } = DefaultPrimaryUsageWarningThresholdPercentage;

    [ObservableProperty]
    public partial int SecondaryUsageWarningThresholdPercentage { get; set; } = DefaultSecondaryUsageWarningThresholdPercentage;
}
