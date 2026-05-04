using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class ProviderAccountViewModel(ProviderAccount providerAccount, ApplicationSettings applicationSettings) : ObservableObject
{
    private static readonly TimeSpan s_primaryUsageWindowDuration = TimeSpan.FromHours(5);
    private static readonly TimeSpan s_primaryUsageAverageUnitDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan s_secondaryUsageWindowDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan s_secondaryUsageAverageUnitDuration = TimeSpan.FromDays(1);

    private readonly ApplicationSettings _applicationSettings = applicationSettings;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ProviderAccount ProviderAccount { get; private set; } = providerAccount;

    public CliProviderKind ProviderKind => ProviderAccount.ProviderKind;

    public ProviderUsageSnapshot ProviderUsageSnapshot => ProviderAccount.LastProviderUsageSnapshot ?? new ProviderUsageSnapshot { ProviderKind = ProviderKind };

    public string AccountIdentifier => ProviderAccount.AccountIdentifier;

    public string CustomAlias => ProviderAccount.CustomAlias;

    public string DisplayName => ProviderAccount.DisplayName;

    public string EmailAddress => ProviderAccount.EmailAddress;

    public string PlanType => ProviderAccount.PlanType;

    public string PlanFilterKey => !string.IsNullOrWhiteSpace(PlanType) && !string.Equals(PlanType, "Unknown", StringComparison.OrdinalIgnoreCase) ? PlanType.Trim().ToLowerInvariant() : "";

    public string PlanText => ProviderKind == CliProviderKind.Codex ? FormatCodexPlanText(PlanType) : FormatClaudeCodePlanText(PlanType);

    public bool IsActive => ProviderAccount.IsActive;

    public bool IsTokenExpired => ProviderAccount.IsTokenExpired;

    public string StatusText => IsTokenExpired ? GetLocalizedString("ProviderAccountViewModel_TokenExpiredStatus") : IsActive ? GetLocalizedString("ProviderAccountViewModel_ActiveStatus") : GetLocalizedString("ProviderAccountViewModel_WaitingStatus");

    public string AccessTokenPreview => ProviderKind == CliProviderKind.Codex && string.IsNullOrWhiteSpace(ProviderAccount.AccountDetailText) ? GetLocalizedString("ProviderAccountViewModel_NoAccessToken") : ProviderAccount.AccountDetailText;

    public string PrimaryUsageText => FormatUsageWindow(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageText => FormatUsageWindow(ProviderUsageSnapshot.SevenDay);

    public string PrimaryUsageWindowLabelText => GetLocalizedString("ProviderAccountViewModel_PrimaryUsageWindowLabel");

    public string SecondaryUsageWindowLabelText => GetLocalizedString("ProviderAccountViewModel_SecondaryUsageWindowLabel");

    public string PrimaryUsageRemainingText => FormatUsageRemaining(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageRemainingText => FormatUsageRemaining(ProviderUsageSnapshot.SevenDay);

    public string PrimaryUsageResetText => FormatUsageReset(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageResetText => FormatUsageReset(ProviderUsageSnapshot.SevenDay);

    public int PrimaryUsageRemainingPercentage => ClampUsageRemainingPercentage(ProviderUsageSnapshot.FiveHour);

    public int SecondaryUsageRemainingPercentage => ClampUsageRemainingPercentage(ProviderUsageSnapshot.SevenDay);

    public bool IsPrimaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(ProviderUsageSnapshot.FiveHour, _applicationSettings.PrimaryUsageWarningThresholdPercentage);

    public bool IsSecondaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(ProviderUsageSnapshot.SevenDay, _applicationSettings.SecondaryUsageWarningThresholdPercentage);

    public bool IsPrimaryUsageOverAverageRateLimit => IsUsageOverAverageRateLimit(ProviderUsageSnapshot.FiveHour, s_primaryUsageWindowDuration, s_primaryUsageAverageUnitDuration);

    public bool IsSecondaryUsageOverAverageRateLimit => IsUsageOverAverageRateLimit(ProviderUsageSnapshot.SevenDay, s_secondaryUsageWindowDuration, s_secondaryUsageAverageUnitDuration);

    public string LastUsageRefreshText => GetFormattedString("ProviderAccountViewModel_LastUsageRefreshFormat", ProviderAccount.LastUsageRefreshTime is null ? GetLocalizedString("ProviderAccountViewModel_NotRefreshed") : ProviderAccount.LastUsageRefreshTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

    public string SearchText => $"{DisplayName} {EmailAddress} {PlanText} {AccountIdentifier} {ProviderAccount.ProviderAccountIdentifier}";

    public bool CanRename => ProviderKind == CliProviderKind.Codex;

    public void Update(ProviderAccount providerAccount)
    {
        ProviderAccount = providerAccount;
        RefreshAccountProperties();
    }

    public void RefreshAccountProperties()
    {
        OnPropertyChanged(nameof(ProviderAccount));
        OnPropertyChanged(nameof(ProviderKind));
        OnPropertyChanged(nameof(ProviderUsageSnapshot));
        OnPropertyChanged(nameof(AccountIdentifier));
        OnPropertyChanged(nameof(CustomAlias));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(EmailAddress));
        OnPropertyChanged(nameof(PlanType));
        OnPropertyChanged(nameof(PlanFilterKey));
        OnPropertyChanged(nameof(PlanText));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsTokenExpired));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AccessTokenPreview));
        OnPropertyChanged(nameof(PrimaryUsageText));
        OnPropertyChanged(nameof(SecondaryUsageText));
        OnPropertyChanged(nameof(PrimaryUsageWindowLabelText));
        OnPropertyChanged(nameof(SecondaryUsageWindowLabelText));
        OnPropertyChanged(nameof(PrimaryUsageRemainingText));
        OnPropertyChanged(nameof(SecondaryUsageRemainingText));
        OnPropertyChanged(nameof(PrimaryUsageResetText));
        OnPropertyChanged(nameof(SecondaryUsageResetText));
        OnPropertyChanged(nameof(PrimaryUsageRemainingPercentage));
        OnPropertyChanged(nameof(SecondaryUsageRemainingPercentage));
        OnPropertyChanged(nameof(IsPrimaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsSecondaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsPrimaryUsageOverAverageRateLimit));
        OnPropertyChanged(nameof(IsSecondaryUsageOverAverageRateLimit));
        OnPropertyChanged(nameof(LastUsageRefreshText));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(CanRename));
    }

    public void RefreshUsageWarningThresholdProperties()
    {
        OnPropertyChanged(nameof(IsPrimaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsSecondaryUsageUnderWarningThreshold));
    }

    private static string FormatClaudeCodePlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? "Unknown" : planType;

    private static string FormatCodexPlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? GetLocalizedString("ProviderAccountViewModel_UnknownPlan") : string.Equals(planType, "prolite", StringComparison.OrdinalIgnoreCase) ? GetLocalizedString("AccountsPage_ProLitePlanFilterSelectorBarItem/Text") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(planType.ToLowerInvariant());

    private static string FormatUsageWindow(ProviderUsageWindow providerUsageWindow)
    {
        if (providerUsageWindow.RemainingPercentage < 0) return GetLocalizedString("ProviderAccountViewModel_UnknownUsage");

        var resetText = FormatUsageReset(providerUsageWindow);
        return GetFormattedString("ProviderAccountViewModel_UsageRemainingFormat", providerUsageWindow.RemainingPercentage, resetText);
    }

    private static string FormatUsageRemaining(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? GetLocalizedString("ProviderAccountViewModel_UnknownUsage") : GetFormattedString("ProviderAccountViewModel_UsageRemainingOnlyFormat", providerUsageWindow.RemainingPercentage);

    private static string FormatUsageReset(ProviderUsageWindow providerUsageWindow)
    {
        if (providerUsageWindow.ResetAfterSeconds < 0) return GetLocalizedString("ProviderAccountViewModel_UnknownResetTime");

        var resetAfterTimeSpan = TimeSpan.FromSeconds(providerUsageWindow.ResetAfterSeconds);
        var wholeDayCount = resetAfterTimeSpan.Days;
        if (wholeDayCount == 1) return GetFormattedString("ProviderAccountViewModel_ResetAfterWithSingleDayFormat", resetAfterTimeSpan);
        if (wholeDayCount > 1) return GetFormattedString("ProviderAccountViewModel_ResetAfterWithMultipleDaysFormat", wholeDayCount, resetAfterTimeSpan);
        return GetFormattedString("ProviderAccountViewModel_ResetAfterFormat", resetAfterTimeSpan);
    }

    private static int ClampUsageRemainingPercentage(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? 0 : Math.Clamp(providerUsageWindow.RemainingPercentage, 0, 100);

    private static bool IsUsageUnderWarningThreshold(ProviderUsageWindow providerUsageWindow, int usageWarningThresholdPercentage) => providerUsageWindow.RemainingPercentage >= 0 && providerUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private static bool IsUsageOverAverageRateLimit(ProviderUsageWindow providerUsageWindow, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration) => IsUsageOverAverageRateLimit(providerUsageWindow.UsedPercentage, providerUsageWindow.ResetAfterSeconds, usageWindowDuration, averageUnitDuration);

    private static bool IsUsageOverAverageRateLimit(int usedPercentage, long resetAfterSeconds, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration)
    {
        if (usedPercentage < 0 || usedPercentage > 100 || resetAfterSeconds < 0) return false;
        if (usageWindowDuration <= TimeSpan.Zero || averageUnitDuration <= TimeSpan.Zero) return false;

        var resetAfterDuration = TimeSpan.FromSeconds(resetAfterSeconds);
        if (resetAfterDuration >= usageWindowDuration) return false;

        var elapsedDuration = usageWindowDuration - resetAfterDuration;
        if (elapsedDuration <= TimeSpan.Zero) return false;

        var usageWindowAverageUnitCount = usageWindowDuration.TotalSeconds / averageUnitDuration.TotalSeconds;
        if (usageWindowAverageUnitCount <= 0) return false;

        var elapsedAverageUnitCount = elapsedDuration.TotalSeconds / averageUnitDuration.TotalSeconds;
        elapsedAverageUnitCount = Math.Min(Math.Max(elapsedAverageUnitCount, 1.0), usageWindowAverageUnitCount);

        var averageUsageLimitPercentage = 100.0 / usageWindowAverageUnitCount;
        var currentAverageUsagePercentage = usedPercentage / elapsedAverageUnitCount;
        return currentAverageUsagePercentage > averageUsageLimitPercentage;
    }

    private static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
