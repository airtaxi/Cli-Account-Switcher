using CodexAccountSwitch.Api.Models.Usage;
using CodexAccountSwitch.WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace CodexAccountSwitch.WinUI.ViewModels;

public sealed partial class CodexAccountViewModel(CodexAccount codexAccount, ApplicationSettings applicationSettings) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountIdentifier))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EmailAddress))]
    [NotifyPropertyChangedFor(nameof(PlanType))]
    [NotifyPropertyChangedFor(nameof(PlanFilterKey))]
    [NotifyPropertyChangedFor(nameof(PlanText))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsTokenExpired))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(AccessTokenPreview))]
    [NotifyPropertyChangedFor(nameof(PrimaryUsageText))]
    [NotifyPropertyChangedFor(nameof(SecondaryUsageText))]
    [NotifyPropertyChangedFor(nameof(PrimaryUsageWindowLabelText))]
    [NotifyPropertyChangedFor(nameof(SecondaryUsageWindowLabelText))]
    [NotifyPropertyChangedFor(nameof(PrimaryUsageRemainingText))]
    [NotifyPropertyChangedFor(nameof(SecondaryUsageRemainingText))]
    [NotifyPropertyChangedFor(nameof(PrimaryUsageResetText))]
    [NotifyPropertyChangedFor(nameof(SecondaryUsageResetText))]
    [NotifyPropertyChangedFor(nameof(PrimaryUsageRemainingPercentage))]
    [NotifyPropertyChangedFor(nameof(SecondaryUsageRemainingPercentage))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryUsageUnderWarningThreshold))]
    [NotifyPropertyChangedFor(nameof(IsSecondaryUsageUnderWarningThreshold))]
    [NotifyPropertyChangedFor(nameof(LastUsageRefreshText))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial CodexAccount CodexAccount { get; set; } = codexAccount;

    public string AccountIdentifier => CodexAccount.AccountIdentifier;

    public string DisplayName => CodexAccount.DisplayName;

    public string EmailAddress => CodexAccount.EmailAddress;

    public string PlanType => CodexAccount.PlanType;

    public string PlanFilterKey => string.IsNullOrWhiteSpace(PlanType) ? "" : PlanType.Trim().ToLowerInvariant();

    public string PlanText => string.IsNullOrWhiteSpace(PlanType) ? GetLocalizedString("CodexAccountViewModel_UnknownPlan") : PlanType == "prolite" ? GetLocalizedString("AccountsPage_ProLitePlanFilterSelectorBarItem/Text") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(PlanType.ToLowerInvariant());

    public bool IsActive => CodexAccount.IsActive;

    public bool IsTokenExpired => CodexAccount.IsTokenExpired;

    public string StatusText => IsTokenExpired ? GetLocalizedString("CodexAccountViewModel_TokenExpiredStatus") : IsActive ? GetLocalizedString("CodexAccountViewModel_ActiveStatus") : GetLocalizedString("CodexAccountViewModel_WaitingStatus");

    public string AccessTokenPreview => BuildAccessTokenPreview(CodexAccount.CodexAuthenticationDocument.GetEffectiveAccessToken());

    public string PrimaryUsageText => FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow);

    public string SecondaryUsageText => FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow);

    public string PrimaryUsageWindowLabelText => GetLocalizedString("CodexAccountViewModel_PrimaryUsageWindowLabel");

    public string SecondaryUsageWindowLabelText => GetLocalizedString("CodexAccountViewModel_SecondaryUsageWindowLabel");

    public string PrimaryUsageRemainingText => FormatUsageRemaining(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow);

    public string SecondaryUsageRemainingText => FormatUsageRemaining(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow);

    public string PrimaryUsageResetText => FormatUsageReset(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow);

    public string SecondaryUsageResetText => FormatUsageReset(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow);

    public int PrimaryUsageRemainingPercentage => ClampUsageRemainingPercentage(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow);

    public int SecondaryUsageRemainingPercentage => ClampUsageRemainingPercentage(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow);

    public bool IsPrimaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow, applicationSettings.PrimaryUsageWarningThresholdPercentage);

    public bool IsSecondaryUsageUnderWarningThreshold => IsUsageUnderWarningThreshold(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow, applicationSettings.SecondaryUsageWarningThresholdPercentage);

    public string LastUsageRefreshText => GetFormattedString("CodexAccountViewModel_LastUsageRefreshFormat", CodexAccount.LastUsageRefreshTime is null ? GetLocalizedString("CodexAccountViewModel_NotRefreshed") : CodexAccount.LastUsageRefreshTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

    public string SearchText => $"{DisplayName} {EmailAddress} {PlanText} {AccountIdentifier}";

    public void Update(CodexAccount codexAccount)
    {
        // The service mutates CodexAccount in place, so the generated setter will not notify calculated properties when the reference is unchanged.
        if (!ReferenceEquals(CodexAccount, codexAccount)) CodexAccount = codexAccount;
        else RefreshCodexAccountProperties();
    }

    private void RefreshCodexAccountProperties()
    {
        OnPropertyChanged(nameof(CodexAccount));
        OnPropertyChanged(nameof(AccountIdentifier));
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
        OnPropertyChanged(nameof(LastUsageRefreshText));
        OnPropertyChanged(nameof(SearchText));
    }

    public void RefreshUsageWarningThresholdProperties()
    {
        OnPropertyChanged(nameof(IsPrimaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsSecondaryUsageUnderWarningThreshold));
    }

    private static string BuildAccessTokenPreview(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return GetLocalizedString("CodexAccountViewModel_NoAccessToken");
        return accessToken.Length <= 18 ? accessToken : $"{accessToken[..8]}...{accessToken[^6..]}";
    }

    private static string FormatUsageWindow(CodexUsageWindow codexUsageWindow)
    {
        if (codexUsageWindow.RemainingPercentage < 0) return GetLocalizedString("CodexAccountViewModel_UnknownUsage");

        var resetText = FormatUsageReset(codexUsageWindow);
        return GetFormattedString("CodexAccountViewModel_UsageRemainingFormat", codexUsageWindow.RemainingPercentage, resetText);
    }

    private static string FormatUsageRemaining(CodexUsageWindow codexUsageWindow) => codexUsageWindow.RemainingPercentage < 0 ? GetLocalizedString("CodexAccountViewModel_UnknownUsage") : GetFormattedString("CodexAccountViewModel_UsageRemainingOnlyFormat", codexUsageWindow.RemainingPercentage);

    private static string FormatUsageReset(CodexUsageWindow codexUsageWindow)
    {
        if (codexUsageWindow.ResetAfterSeconds < 0) return GetLocalizedString("CodexAccountViewModel_UnknownResetTime");

        var resetAfterTimeSpan = TimeSpan.FromSeconds(codexUsageWindow.ResetAfterSeconds);
        var wholeDayCount = resetAfterTimeSpan.Days;
        if (wholeDayCount == 1) return GetFormattedString("CodexAccountViewModel_ResetAfterWithSingleDayFormat", resetAfterTimeSpan);
        if (wholeDayCount > 1) return GetFormattedString("CodexAccountViewModel_ResetAfterWithMultipleDaysFormat", wholeDayCount, resetAfterTimeSpan);
        return GetFormattedString("CodexAccountViewModel_ResetAfterFormat", resetAfterTimeSpan);
    }

    private static int ClampUsageRemainingPercentage(CodexUsageWindow codexUsageWindow) => codexUsageWindow.RemainingPercentage < 0 ? 0 : Math.Clamp(codexUsageWindow.RemainingPercentage, 0, 100);

    private static bool IsUsageUnderWarningThreshold(CodexUsageWindow codexUsageWindow, int usageWarningThresholdPercentage) => codexUsageWindow.RemainingPercentage >= 0 && codexUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
