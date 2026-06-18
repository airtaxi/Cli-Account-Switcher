using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class ProviderAccountViewModel(ProviderAccount providerAccount, ApplicationSettings applicationSettings, LocalizationService localizationService) : ObservableObject
{
    private static readonly TimeSpan s_primaryUsageWindowDuration = TimeSpan.FromHours(5);
    private static readonly TimeSpan s_primaryUsageAverageUnitDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan s_secondaryUsageWindowDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan s_secondaryUsageAverageUnitDuration = TimeSpan.FromDays(1);

    private readonly ApplicationSettings _applicationSettings = applicationSettings;
    private readonly LocalizationService _localizationService = localizationService;
    private DateTimeOffset? _primaryUsageResetTime = GetUsageResetTime(GetProviderUsageSnapshot(providerAccount).FiveHour, providerAccount.LastUsageRefreshTime);
    private DateTimeOffset? _secondaryUsageResetTime = GetUsageResetTime(GetProviderUsageSnapshot(providerAccount).SevenDay, providerAccount.LastUsageRefreshTime);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ProviderAccount ProviderAccount { get; private set; } = providerAccount;

    public CliProviderKind ProviderKind => ProviderAccount.ProviderKind;

    public ProviderUsageSnapshot ProviderUsageSnapshot => GetProviderUsageSnapshot(ProviderAccount);

    public string AccountIdentifier => ProviderAccount.AccountIdentifier;

    public string CustomAlias => ProviderAccount.CustomAlias;

    public string DisplayName => ProviderAccount.DisplayName;

    public string EmailAddress => ProviderAccount.EmailAddress;

    public bool IsEmailAddressVisible => ProviderKind != CliProviderKind.Zai;

    public string PlanType => ProviderAccount.PlanType;

    public string PlanFilterKey => !string.IsNullOrWhiteSpace(PlanType) && !string.Equals(PlanType, "Unknown", StringComparison.OrdinalIgnoreCase) ? PlanType.Trim().ToLowerInvariant() : "";

    public string PlanText => ProviderKind == CliProviderKind.Codex ? FormatCodexPlanText(PlanType) : ProviderKind == CliProviderKind.Zai ? FormatZaiPlanText(PlanType) : FormatClaudeCodePlanText(PlanType);

    public bool IsActive => ProviderAccount.IsActive;

    public bool IsTokenExpired => ProviderAccount.IsTokenExpired;

    public string StatusText => IsTokenExpired ? _localizationService.GetLocalizedString("ProviderAccountViewModel_TokenExpiredStatus") : IsActive ? _localizationService.GetLocalizedString("ProviderAccountViewModel_ActiveStatus") : _localizationService.GetLocalizedString("ProviderAccountViewModel_WaitingStatus");

    public string AccessTokenPreview => ProviderKind == CliProviderKind.Codex && string.IsNullOrWhiteSpace(ProviderAccount.AccountDetailText) ? _localizationService.GetLocalizedString("ProviderAccountViewModel_NoAccessToken") : ProviderAccount.AccountDetailText;

    public string PrimaryUsageText => FormatUsageWindow(ProviderUsageSnapshot.FiveHour, _primaryUsageResetTime);

    public string SecondaryUsageText => FormatUsageWindow(ProviderUsageSnapshot.SevenDay, _secondaryUsageResetTime);

    public string PrimaryUsageWindowLabelText => _localizationService.GetLocalizedString("ProviderAccountViewModel_PrimaryUsageWindowLabel");

    public string SecondaryUsageWindowLabelText => _localizationService.GetLocalizedString("ProviderAccountViewModel_SecondaryUsageWindowLabel");

    public string PrimaryUsageRemainingText => FormatUsageRemaining(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageRemainingText => FormatUsageRemaining(ProviderUsageSnapshot.SevenDay);

    public string PrimaryUsageResetText => FormatUsageReset(_primaryUsageResetTime);

    public string SecondaryUsageResetText => FormatUsageReset(_secondaryUsageResetTime);

    public int PrimaryUsageRemainingPercentage => ClampUsageRemainingPercentage(ProviderUsageSnapshot.FiveHour);

    public int SecondaryUsageRemainingPercentage => ClampUsageRemainingPercentage(ProviderUsageSnapshot.SevenDay);

    public bool IsPrimaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(ProviderUsageSnapshot.FiveHour, _applicationSettings.PrimaryUsageWarningThresholdPercentage);

    public bool IsSecondaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(ProviderUsageSnapshot.SevenDay, _applicationSettings.SecondaryUsageWarningThresholdPercentage);

    public bool IsPrimaryUsageOverAverageRateLimit => PrimaryUsageAverageRateLimitExceededPercentage > 0;

    public bool IsSecondaryUsageOverAverageRateLimit => SecondaryUsageAverageRateLimitExceededPercentage > 0;

    public int PrimaryUsageAverageRateLimitExceededPercentage => CalculateUsageAverageRateLimitExceededPercentage(ProviderUsageSnapshot.FiveHour, s_primaryUsageWindowDuration, s_primaryUsageAverageUnitDuration);

    public int SecondaryUsageAverageRateLimitExceededPercentage => CalculateUsageAverageRateLimitExceededPercentage(ProviderUsageSnapshot.SevenDay, s_secondaryUsageWindowDuration, s_secondaryUsageAverageUnitDuration);

    public int PrimaryUsageAverageRateLimitHeadroomPercentage => CalculateUsageAverageRateLimitHeadroomPercentage(ProviderUsageSnapshot.FiveHour, s_primaryUsageWindowDuration, s_primaryUsageAverageUnitDuration);

    public int SecondaryUsageAverageRateLimitHeadroomPercentage => CalculateUsageAverageRateLimitHeadroomPercentage(ProviderUsageSnapshot.SevenDay, s_secondaryUsageWindowDuration, s_secondaryUsageAverageUnitDuration);

    public bool IsPrimaryUsageAtAverageRateLimit => PrimaryUsageAverageRateLimitExceededPercentage == 0 && PrimaryUsageAverageRateLimitHeadroomPercentage == 0;

    public bool IsSecondaryUsageAtAverageRateLimit => SecondaryUsageAverageRateLimitExceededPercentage == 0 && SecondaryUsageAverageRateLimitHeadroomPercentage == 0;

    public bool HasPrimaryUsageAverageRateLimitHeadroom => PrimaryUsageAverageRateLimitHeadroomPercentage > 0;

    public bool HasSecondaryUsageAverageRateLimitHeadroom => SecondaryUsageAverageRateLimitHeadroomPercentage > 0;

    public string PrimaryUsageAverageRateStatusText => FormatUsageAverageRateStatus(PrimaryUsageAverageRateLimitExceededPercentage, PrimaryUsageAverageRateLimitHeadroomPercentage);

    public string SecondaryUsageAverageRateStatusText => FormatUsageAverageRateStatus(SecondaryUsageAverageRateLimitExceededPercentage, SecondaryUsageAverageRateLimitHeadroomPercentage);

    public string LastUsageRefreshText => _localizationService.GetFormattedString("ProviderAccountViewModel_LastUsageRefreshFormat", ProviderAccount.LastUsageRefreshTime is null ? _localizationService.GetLocalizedString("ProviderAccountViewModel_NotRefreshed") : ProviderAccount.LastUsageRefreshTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

    public string SearchText => $"{DisplayName} {EmailAddress} {PlanText} {AccountIdentifier} {ProviderAccount.ProviderAccountIdentifier}";

    public bool CanRename => ProviderKind is CliProviderKind.Codex or CliProviderKind.Zai;

    public void Update(ProviderAccount providerAccount)
    {
        ProviderAccount = providerAccount;
        _primaryUsageResetTime = GetUsageResetTime(ProviderUsageSnapshot.FiveHour, ProviderAccount.LastUsageRefreshTime);
        _secondaryUsageResetTime = GetUsageResetTime(ProviderUsageSnapshot.SevenDay, ProviderAccount.LastUsageRefreshTime);
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
        OnPropertyChanged(nameof(IsEmailAddressVisible));
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
        OnPropertyChanged(nameof(PrimaryUsageAverageRateLimitExceededPercentage));
        OnPropertyChanged(nameof(SecondaryUsageAverageRateLimitExceededPercentage));
        OnPropertyChanged(nameof(PrimaryUsageAverageRateLimitHeadroomPercentage));
        OnPropertyChanged(nameof(SecondaryUsageAverageRateLimitHeadroomPercentage));
        OnPropertyChanged(nameof(IsPrimaryUsageAtAverageRateLimit));
        OnPropertyChanged(nameof(IsSecondaryUsageAtAverageRateLimit));
        OnPropertyChanged(nameof(HasPrimaryUsageAverageRateLimitHeadroom));
        OnPropertyChanged(nameof(HasSecondaryUsageAverageRateLimitHeadroom));
        OnPropertyChanged(nameof(PrimaryUsageAverageRateStatusText));
        OnPropertyChanged(nameof(SecondaryUsageAverageRateStatusText));
        OnPropertyChanged(nameof(LastUsageRefreshText));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(CanRename));
    }

    public void RefreshUsageWarningThresholdProperties()
    {
        OnPropertyChanged(nameof(IsPrimaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsSecondaryUsageUnderWarningThreshold));
    }

    public void RefreshUsageResetTextProperties()
    {
        OnPropertyChanged(nameof(PrimaryUsageText));
        OnPropertyChanged(nameof(SecondaryUsageText));
        OnPropertyChanged(nameof(PrimaryUsageResetText));
        OnPropertyChanged(nameof(SecondaryUsageResetText));
    }

    private static string FormatClaudeCodePlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? "Unknown" : planType;

    private string FormatZaiPlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownPlan") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(planType.ToLowerInvariant());

    private string FormatCodexPlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownPlan") : string.Equals(planType, "prolite", StringComparison.OrdinalIgnoreCase) ? _localizationService.GetLocalizedString("AccountsPage_ProLitePlanFilterSelectorBarItem/Text") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(planType.ToLowerInvariant());

    private string FormatUsageWindow(ProviderUsageWindow providerUsageWindow, DateTimeOffset? usageResetTime)
    {
        if (providerUsageWindow.RemainingPercentage < 0) return _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownUsage");

        var resetText = FormatUsageReset(usageResetTime);
        return _localizationService.GetFormattedString("ProviderAccountViewModel_UsageRemainingFormat", providerUsageWindow.RemainingPercentage, resetText);
    }

    private string FormatUsageRemaining(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownUsage") : _localizationService.GetFormattedString("ProviderAccountViewModel_UsageRemainingOnlyFormat", providerUsageWindow.RemainingPercentage);

    private string FormatUsageReset(DateTimeOffset? usageResetTime)
    {
        if (usageResetTime is null) return _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownResetTime");

        var resetAfterSeconds = Math.Max(0, Convert.ToInt64(Math.Ceiling((usageResetTime.Value - DateTimeOffset.UtcNow).TotalSeconds)));
        var resetAfterTimeSpan = TimeSpan.FromSeconds(resetAfterSeconds);
        var wholeDayCount = resetAfterTimeSpan.Days;
        if (wholeDayCount == 1) return _localizationService.GetFormattedString("ProviderAccountViewModel_ResetAfterWithSingleDayFormat", resetAfterTimeSpan);
        if (wholeDayCount > 1) return _localizationService.GetFormattedString("ProviderAccountViewModel_ResetAfterWithMultipleDaysFormat", wholeDayCount, resetAfterTimeSpan);
        return _localizationService.GetFormattedString("ProviderAccountViewModel_ResetAfterFormat", resetAfterTimeSpan);
    }

    private string FormatUsageAverageRateStatus(int exceededPercentage, int headroomPercentage) => exceededPercentage > 0 ? _localizationService.GetFormattedString("UsageAverageRateWarningFormat", exceededPercentage) : headroomPercentage > 0 ? _localizationService.GetFormattedString("UsageAverageRateHeadroomFormat", headroomPercentage) : _localizationService.GetLocalizedString("UsageAverageRateAtLimitText");

    private static DateTimeOffset? GetUsageResetTime(ProviderUsageWindow providerUsageWindow, DateTimeOffset? usageRefreshTime)
    {
        if (providerUsageWindow.ResetAt is not null) return providerUsageWindow.ResetAt;
        if (providerUsageWindow.ResetAfterSeconds < 0) return null;
        return (usageRefreshTime ?? DateTimeOffset.UtcNow).AddSeconds(providerUsageWindow.ResetAfterSeconds);
    }

    private static ProviderUsageSnapshot GetProviderUsageSnapshot(ProviderAccount providerAccount) => providerAccount.LastProviderUsageSnapshot ?? new ProviderUsageSnapshot { ProviderKind = providerAccount.ProviderKind };

    private static int ClampUsageRemainingPercentage(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? 0 : Math.Clamp(providerUsageWindow.RemainingPercentage, 0, 100);

    private static bool IsUsageUnderWarningThreshold(ProviderUsageWindow providerUsageWindow, int usageWarningThresholdPercentage) => providerUsageWindow.RemainingPercentage >= 0 && providerUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private static int CalculateUsageAverageRateLimitExceededPercentage(ProviderUsageWindow providerUsageWindow, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration) => CalculateUsageAverageRateLimitExceededPercentage(providerUsageWindow.UsedPercentage, providerUsageWindow.ResetAfterSeconds, usageWindowDuration, averageUnitDuration);

    private static int CalculateUsageAverageRateLimitExceededPercentage(int usedPercentage, long resetAfterSeconds, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration)
    {
        if (!TryCalculateUsageAverageRateDifferencePercentage(usedPercentage, resetAfterSeconds, usageWindowDuration, averageUnitDuration, out var differencePercentage)) return 0;

        var exceededPercentage = differencePercentage;
        return exceededPercentage <= 0 ? 0 : Convert.ToInt32(Math.Ceiling(exceededPercentage));
    }

    private static int CalculateUsageAverageRateLimitHeadroomPercentage(ProviderUsageWindow providerUsageWindow, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration) => CalculateUsageAverageRateLimitHeadroomPercentage(providerUsageWindow.UsedPercentage, providerUsageWindow.ResetAfterSeconds, usageWindowDuration, averageUnitDuration);

    private static int CalculateUsageAverageRateLimitHeadroomPercentage(int usedPercentage, long resetAfterSeconds, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration)
    {
        if (!TryCalculateUsageAverageRateDifferencePercentage(usedPercentage, resetAfterSeconds, usageWindowDuration, averageUnitDuration, out var differencePercentage)) return 0;

        var headroomPercentage = -differencePercentage;
        return headroomPercentage <= 0 ? 0 : Convert.ToInt32(Math.Ceiling(headroomPercentage));
    }

    private static bool TryCalculateUsageAverageRateDifferencePercentage(int usedPercentage, long resetAfterSeconds, TimeSpan usageWindowDuration, TimeSpan averageUnitDuration, out double differencePercentage)
    {
        differencePercentage = 0;

        if (usedPercentage < 0 || usedPercentage > 100 || resetAfterSeconds < 0) return false;
        if (usageWindowDuration <= TimeSpan.Zero || averageUnitDuration <= TimeSpan.Zero) return false;

        var elapsedDuration = usageWindowDuration - TimeSpan.FromSeconds(resetAfterSeconds);
        if (elapsedDuration < TimeSpan.Zero) elapsedDuration = TimeSpan.Zero;

        var averagePaceUsedPercentage = Math.Clamp((elapsedDuration.TotalSeconds / usageWindowDuration.TotalSeconds) * 100.0, 0.0, 100.0);
        differencePercentage = usedPercentage - averagePaceUsedPercentage;
        return true;
    }

    private static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);


}
