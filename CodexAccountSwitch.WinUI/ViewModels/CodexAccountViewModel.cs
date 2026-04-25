using CodexAccountSwitch.Api.Models.Usage;
using CodexAccountSwitch.WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace CodexAccountSwitch.WinUI.ViewModels;

public sealed partial class CodexAccountViewModel(CodexAccount codexAccount) : ObservableObject
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
    [NotifyPropertyChangedFor(nameof(LastUsageRefreshText))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial CodexAccount CodexAccount { get; set; } = codexAccount;

    public string AccountIdentifier => CodexAccount.AccountIdentifier;

    public string DisplayName => CodexAccount.DisplayName;

    public string EmailAddress => CodexAccount.EmailAddress;

    public string PlanType => CodexAccount.PlanType;

    public string PlanFilterKey => string.IsNullOrWhiteSpace(PlanType) ? "" : PlanType.Trim().ToLowerInvariant();

    public string PlanText => string.IsNullOrWhiteSpace(PlanType) ? GetLocalizedString("CodexAccountViewModel_UnknownPlan") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(PlanType.ToLowerInvariant());

    public bool IsActive => CodexAccount.IsActive;

    public bool IsTokenExpired => CodexAccount.IsTokenExpired;

    public string StatusText => IsTokenExpired ? GetLocalizedString("CodexAccountViewModel_TokenExpiredStatus") : IsActive ? GetLocalizedString("CodexAccountViewModel_ActiveStatus") : GetLocalizedString("CodexAccountViewModel_WaitingStatus");

    public string AccessTokenPreview => BuildAccessTokenPreview(CodexAccount.CodexAuthenticationDocument.GetEffectiveAccessToken());

    public string PrimaryUsageText => FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow);

    public string SecondaryUsageText => FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow);

    public string LastUsageRefreshText => CodexAccount.LastUsageRefreshTime is null ? GetLocalizedString("CodexAccountViewModel_NotRefreshed") : CodexAccount.LastUsageRefreshTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string SearchText => $"{DisplayName} {EmailAddress} {PlanText} {AccountIdentifier}";

    public void Update(CodexAccount codexAccount) => CodexAccount = codexAccount;

    private static string BuildAccessTokenPreview(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return GetLocalizedString("CodexAccountViewModel_NoAccessToken");
        return accessToken.Length <= 18 ? accessToken : $"{accessToken[..8]}...{accessToken[^6..]}";
    }

    private static string FormatUsageWindow(CodexUsageWindow codexUsageWindow)
    {
        if (codexUsageWindow.RemainingPercentage < 0) return GetLocalizedString("CodexAccountViewModel_UnknownUsage");

        var resetText = codexUsageWindow.ResetAfterSeconds < 0 ? GetLocalizedString("CodexAccountViewModel_UnknownResetTime") : GetFormattedString("CodexAccountViewModel_ResetAfterFormat", TimeSpan.FromSeconds(codexUsageWindow.ResetAfterSeconds));
        return GetFormattedString("CodexAccountViewModel_UsageRemainingFormat", codexUsageWindow.RemainingPercentage, resetText);
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
