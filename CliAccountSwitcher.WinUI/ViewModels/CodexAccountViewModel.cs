using CliAccountSwitcher.Api.Models.Usage;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class CodexAccountViewModel : ObservableObject
{
    private readonly ApplicationSettings _applicationSettings;
    private DateTimeOffset? _providerUsageRefreshTime;

    public CodexAccountViewModel(CodexAccount codexAccount, ApplicationSettings applicationSettings)
    {
        _applicationSettings = applicationSettings;
        ProviderKind = CliProviderKind.Codex;
        CodexAccount = codexAccount;
    }

    public CodexAccountViewModel(StoredProviderAccount storedProviderAccount, ApplicationSettings applicationSettings)
    {
        _applicationSettings = applicationSettings;
        ProviderKind = CliProviderKind.ClaudeCode;
        StoredProviderAccount = storedProviderAccount;
        ProviderUsageSnapshot = new ProviderUsageSnapshot { ProviderKind = CliProviderKind.ClaudeCode };
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public CliProviderKind ProviderKind { get; private set; } = CliProviderKind.Codex;

    public CodexAccount CodexAccount { get; private set; } = new();

    public StoredProviderAccount StoredProviderAccount { get; private set; } = new();

    public ProviderUsageSnapshot ProviderUsageSnapshot { get; private set; } = new();

    public string AccountIdentifier => ProviderKind == CliProviderKind.Codex ? CodexAccount.AccountIdentifier : StoredProviderAccount.StoredAccountIdentifier;

    public string CustomAlias => ProviderKind == CliProviderKind.Codex ? CodexAccount.CustomAlias : "";

    public string DisplayName => ProviderKind == CliProviderKind.Codex ? CodexAccount.DisplayName : string.IsNullOrWhiteSpace(StoredProviderAccount.DisplayName) ? StoredProviderAccount.EmailAddress : StoredProviderAccount.DisplayName;

    public string EmailAddress => ProviderKind == CliProviderKind.Codex ? CodexAccount.EmailAddress : StoredProviderAccount.EmailAddress;

    public string PlanType => ProviderKind == CliProviderKind.Codex ? CodexAccount.PlanType : StoredProviderAccount.OrganizationName;

    public string PlanFilterKey => ProviderKind == CliProviderKind.Codex && !string.IsNullOrWhiteSpace(PlanType) ? PlanType.Trim().ToLowerInvariant() : "";

    public string PlanText => ProviderKind == CliProviderKind.Codex ? FormatCodexPlanText(PlanType) : FormatClaudeCodeOrganizationText();

    public bool IsActive => ProviderKind == CliProviderKind.Codex ? CodexAccount.IsActive : StoredProviderAccount.IsActive;

    public bool IsTokenExpired => ProviderKind == CliProviderKind.Codex ? CodexAccount.IsTokenExpired : StoredProviderAccount.IsTokenExpired;

    public string StatusText => IsTokenExpired ? GetLocalizedString("CodexAccountViewModel_TokenExpiredStatus") : IsActive ? GetLocalizedString("CodexAccountViewModel_ActiveStatus") : GetLocalizedString("CodexAccountViewModel_WaitingStatus");

    public string AccessTokenPreview => ProviderKind == CliProviderKind.Codex ? BuildAccessTokenPreview(CodexAccount.CodexAuthenticationDocument.GetEffectiveAccessToken()) : FormatClaudeCodeAccountDetailText();

    public string PrimaryUsageText => ProviderKind == CliProviderKind.Codex ? FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow) : FormatUsageWindow(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageText => ProviderKind == CliProviderKind.Codex ? FormatUsageWindow(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow) : FormatUsageWindow(ProviderUsageSnapshot.SevenDay);

    public string PrimaryUsageWindowLabelText => GetLocalizedString("CodexAccountViewModel_PrimaryUsageWindowLabel");

    public string SecondaryUsageWindowLabelText => GetLocalizedString("CodexAccountViewModel_SecondaryUsageWindowLabel");

    public string PrimaryUsageRemainingText => ProviderKind == CliProviderKind.Codex ? FormatUsageRemaining(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow) : FormatUsageRemaining(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageRemainingText => ProviderKind == CliProviderKind.Codex ? FormatUsageRemaining(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow) : FormatUsageRemaining(ProviderUsageSnapshot.SevenDay);

    public string PrimaryUsageResetText => ProviderKind == CliProviderKind.Codex ? FormatUsageReset(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow) : FormatUsageReset(ProviderUsageSnapshot.FiveHour);

    public string SecondaryUsageResetText => ProviderKind == CliProviderKind.Codex ? FormatUsageReset(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow) : FormatUsageReset(ProviderUsageSnapshot.SevenDay);

    public int PrimaryUsageRemainingPercentage => ProviderKind == CliProviderKind.Codex ? ClampUsageRemainingPercentage(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow) : ClampUsageRemainingPercentage(ProviderUsageSnapshot.FiveHour);

    public int SecondaryUsageRemainingPercentage => ProviderKind == CliProviderKind.Codex ? ClampUsageRemainingPercentage(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow) : ClampUsageRemainingPercentage(ProviderUsageSnapshot.SevenDay);

    public bool IsPrimaryUsageUnderWarningThreshold => ProviderKind == CliProviderKind.Codex ? IsUsageUnderWarningThreshold(CodexAccount.LastCodexUsageSnapshot.PrimaryWindow, _applicationSettings.PrimaryUsageWarningThresholdPercentage) : IsUsageUnderWarningThreshold(ProviderUsageSnapshot.FiveHour, _applicationSettings.PrimaryUsageWarningThresholdPercentage);

    public bool IsSecondaryUsageUnderWarningThreshold => ProviderKind == CliProviderKind.Codex ? IsUsageUnderWarningThreshold(CodexAccount.LastCodexUsageSnapshot.SecondaryWindow, _applicationSettings.SecondaryUsageWarningThresholdPercentage) : IsUsageUnderWarningThreshold(ProviderUsageSnapshot.SevenDay, _applicationSettings.SecondaryUsageWarningThresholdPercentage);

    public string LastUsageRefreshText => ProviderKind == CliProviderKind.Codex
        ? GetFormattedString("CodexAccountViewModel_LastUsageRefreshFormat", CodexAccount.LastUsageRefreshTime is null ? GetLocalizedString("CodexAccountViewModel_NotRefreshed") : CodexAccount.LastUsageRefreshTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture))
        : GetFormattedString("CodexAccountViewModel_LastUpdatedFormat", (_providerUsageRefreshTime ?? StoredProviderAccount.LastUpdated).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

    public string SearchText => $"{DisplayName} {EmailAddress} {PlanText} {AccountIdentifier} {StoredProviderAccount.OrganizationIdentifier}";

    public bool CanRename => ProviderKind == CliProviderKind.Codex;

    public void Update(CodexAccount codexAccount)
    {
        // The service mutates CodexAccount in place, so calculated properties are refreshed explicitly.
        ProviderKind = CliProviderKind.Codex;
        CodexAccount = codexAccount;
        RefreshAccountProperties();
    }

    public void Update(StoredProviderAccount storedProviderAccount)
    {
        ProviderKind = CliProviderKind.ClaudeCode;
        StoredProviderAccount = storedProviderAccount;
        RefreshAccountProperties();
    }

    public void UpdateProviderUsageSnapshot(ProviderUsageSnapshot providerUsageSnapshot) => UpdateProviderUsageSnapshot(providerUsageSnapshot, DateTimeOffset.UtcNow);

    public void UpdateProviderUsageSnapshot(ProviderUsageSnapshot providerUsageSnapshot, DateTimeOffset providerUsageRefreshTime)
    {
        ProviderUsageSnapshot = providerUsageSnapshot;
        _providerUsageRefreshTime = providerUsageRefreshTime;
        RefreshAccountProperties();
    }

    public void ClearProviderUsageSnapshot()
    {
        if (ProviderKind != CliProviderKind.ClaudeCode) return;

        ProviderUsageSnapshot = new ProviderUsageSnapshot { ProviderKind = CliProviderKind.ClaudeCode };
        _providerUsageRefreshTime = null;
        RefreshAccountProperties();
    }

    public void RefreshAccountProperties()
    {
        OnPropertyChanged(nameof(ProviderKind));
        OnPropertyChanged(nameof(CodexAccount));
        OnPropertyChanged(nameof(StoredProviderAccount));
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
        OnPropertyChanged(nameof(LastUsageRefreshText));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(CanRename));
    }

    public void RefreshUsageWarningThresholdProperties()
    {
        OnPropertyChanged(nameof(IsPrimaryUsageUnderWarningThreshold));
        OnPropertyChanged(nameof(IsSecondaryUsageUnderWarningThreshold));
    }

    private string FormatClaudeCodeOrganizationText()
    {
        if (!string.IsNullOrWhiteSpace(StoredProviderAccount.OrganizationName)) return StoredProviderAccount.OrganizationName;
        if (!string.IsNullOrWhiteSpace(StoredProviderAccount.OrganizationIdentifier)) return StoredProviderAccount.OrganizationIdentifier;
        return GetLocalizedString("CodexAccountViewModel_UnknownPlan");
    }

    private string FormatClaudeCodeAccountDetailText()
    {
        if (!string.IsNullOrWhiteSpace(StoredProviderAccount.OrganizationName)) return StoredProviderAccount.OrganizationName;
        if (!string.IsNullOrWhiteSpace(StoredProviderAccount.AccountIdentifier)) return StoredProviderAccount.AccountIdentifier;
        return StoredProviderAccount.StoredAccountIdentifier;
    }

    private static string FormatCodexPlanText(string planType) => string.IsNullOrWhiteSpace(planType) ? GetLocalizedString("CodexAccountViewModel_UnknownPlan") : planType == "prolite" ? GetLocalizedString("AccountsPage_ProLitePlanFilterSelectorBarItem/Text") : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(planType.ToLowerInvariant());

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

    private static string FormatUsageWindow(ProviderUsageWindow providerUsageWindow)
    {
        if (providerUsageWindow.RemainingPercentage < 0) return GetLocalizedString("CodexAccountViewModel_UnknownUsage");

        var resetText = FormatUsageReset(providerUsageWindow);
        return GetFormattedString("CodexAccountViewModel_UsageRemainingFormat", providerUsageWindow.RemainingPercentage, resetText);
    }

    private static string FormatUsageRemaining(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? GetLocalizedString("CodexAccountViewModel_UnknownUsage") : GetFormattedString("CodexAccountViewModel_UsageRemainingOnlyFormat", providerUsageWindow.RemainingPercentage);

    private static string FormatUsageReset(ProviderUsageWindow providerUsageWindow)
    {
        if (providerUsageWindow.ResetAfterSeconds < 0) return GetLocalizedString("CodexAccountViewModel_UnknownResetTime");

        var resetAfterTimeSpan = TimeSpan.FromSeconds(providerUsageWindow.ResetAfterSeconds);
        var wholeDayCount = resetAfterTimeSpan.Days;
        if (wholeDayCount == 1) return GetFormattedString("CodexAccountViewModel_ResetAfterWithSingleDayFormat", resetAfterTimeSpan);
        if (wholeDayCount > 1) return GetFormattedString("CodexAccountViewModel_ResetAfterWithMultipleDaysFormat", wholeDayCount, resetAfterTimeSpan);
        return GetFormattedString("CodexAccountViewModel_ResetAfterFormat", resetAfterTimeSpan);
    }

    private static int ClampUsageRemainingPercentage(ProviderUsageWindow providerUsageWindow) => providerUsageWindow.RemainingPercentage < 0 ? 0 : Math.Clamp(providerUsageWindow.RemainingPercentage, 0, 100);

    private static bool IsUsageUnderWarningThreshold(ProviderUsageWindow providerUsageWindow, int usageWarningThresholdPercentage) => providerUsageWindow.RemainingPercentage >= 0 && providerUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
