using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class SettingsPageViewModel(ApplicationSettings applicationSettings, ApplicationSettingsService applicationSettingsService, ApplicationThemeService applicationThemeService, StartupRegistrationService startupRegistrationService, StoreUpdateService storeUpdateService, FileLogService fileLogService, AccountServiceManager accountServiceManager, LocalizationService localizationService) : ObservableObject
{
    private const int MinimumUsageRefreshIntervalMinutes = 1;
    private const int MaximumUsageRefreshIntervalMinutes = 1440;
    private const int MinimumUsageSurgeNotificationThresholdPercentage = 1;
    private const int MaximumUsageSurgeNotificationThresholdPercentage = 100;
    private const int MinimumUsageSurgeNotificationWindowMinutes = 1;
    private const int MaximumUsageSurgeNotificationWindowMinutes = 300;

    public string ApplicationVersionText { get; } = localizationService.GetFormattedString("SettingsPage_ApplicationVersionFormat", GetCurrentApplicationVersion());

    public string ApplicationSettingsFileTypeChoiceText => localizationService.GetLocalizedString("SettingsPage_ApplicationSettingsFileTypeChoice");

    public string IntegratedLogFileTypeChoiceText => localizationService.GetLocalizedString("SettingsPage_IntegratedLogFileTypeChoice");

    public string ImportApplicationSettingsLoadingMessage => localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsLoadingMessage");

    public string CheckForUpdatesLoadingMessage => localizationService.GetLocalizedString("SettingsPage_CheckForUpdatesLoadingMessage");

    public string ApplicationSettingsSuggestedFileName => $"codex-account-switch-settings-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

    public string IntegratedLogSuggestedFileName => $"codex-account-switch-logs-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

    [ObservableProperty]
    public partial int LanguageSelectedIndex { get; set; } = GetLanguageSelectedIndex(applicationSettings.LanguageOverride);

    [ObservableProperty]
    public partial int ThemeSelectedIndex { get; set; } = GetThemeSelectedIndex(applicationSettings.Theme);

    [ObservableProperty]
    public partial bool IsAutomaticUpdateCheckEnabled { get; set; } = applicationSettings.IsAutomaticUpdateCheckEnabled;

    [ObservableProperty]
    public partial bool IsStartupLaunchEnabled { get; set; } = applicationSettings.IsStartupLaunchEnabled;

    [ObservableProperty]
    public partial bool IsTaskbarUsageVisible { get; set; } = !applicationSettings.HideTaskbarUsage;

    [ObservableProperty]
    public partial bool IsExpiredAccountAutomaticDeletionEnabled { get; set; } = applicationSettings.IsExpiredAccountAutomaticDeletionEnabled;

    [ObservableProperty]
    public partial bool IsExpiredAccountNotificationEnabled { get; set; } = applicationSettings.IsExpiredAccountNotificationEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveAccountUsageRefreshIntervalEnabled))]
    public partial bool IsActiveAccountUsageAutomaticRefreshEnabled { get; set; } = applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;

    [ObservableProperty]
    public partial double ActiveAccountUsageRefreshIntervalMinutes { get; set; } = ConvertSecondsToWholeMinutes(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInactiveAccountUsageRefreshIntervalEnabled))]
    public partial bool IsInactiveAccountUsageAutomaticRefreshEnabled { get; set; } = applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;

    [ObservableProperty]
    public partial double InactiveAccountUsageRefreshIntervalMinutes { get; set; } = ConvertSecondsToWholeMinutes(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds);

    [ObservableProperty]
    public partial double PrimaryUsageWarningThresholdPercentage { get; set; } = applicationSettings.PrimaryUsageWarningThresholdPercentage;

    [ObservableProperty]
    public partial double SecondaryUsageWarningThresholdPercentage { get; set; } = applicationSettings.SecondaryUsageWarningThresholdPercentage;

    [ObservableProperty]
    public partial bool IsPrimaryUsageLowQuotaNotificationEnabled { get; set; } = applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled;

    [ObservableProperty]
    public partial bool IsSecondaryUsageLowQuotaNotificationEnabled { get; set; } = applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryUsageSurgeNotificationSettingsEnabled))]
    public partial bool IsPrimaryUsageSurgeNotificationEnabled { get; set; } = applicationSettings.IsPrimaryUsageSurgeNotificationEnabled;

    [ObservableProperty]
    public partial double PrimaryUsageSurgeNotificationThresholdPercentage { get; set; } = applicationSettings.PrimaryUsageSurgeNotificationThresholdPercentage;

    [ObservableProperty]
    public partial double PrimaryUsageSurgeNotificationWindowMinutes { get; set; } = applicationSettings.PrimaryUsageSurgeNotificationWindowMinutes;

    [ObservableProperty]
    public partial string ActiveAccountNextUsageRefreshText { get; set; } = "";

    [ObservableProperty]
    public partial string InactiveAccountNextUsageRefreshText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsExportApplicationSettingsButtonEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsImportApplicationSettingsButtonEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExportIntegratedLogButtonEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCheckForUpdatesButtonEnabled { get; set; } = true;

    public bool IsActiveAccountUsageRefreshIntervalEnabled => IsActiveAccountUsageAutomaticRefreshEnabled;

    public bool IsInactiveAccountUsageRefreshIntervalEnabled => IsInactiveAccountUsageAutomaticRefreshEnabled;

    public bool IsPrimaryUsageSurgeNotificationSettingsEnabled => IsPrimaryUsageSurgeNotificationEnabled;

    public void Load()
    {
        SynchronizePropertiesFromSettings();
        RefreshUsageRefreshCountdownTexts();
    }

    public bool HasIntegratedLogs() => fileLogService.HasLogs();

    public SettingsPageDialogData CreateIntegratedLogEmptyDialogData() => new(localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogEmptyDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogEmptyDialogMessage"));

    public SettingsPageDialogData CreateIntegratedLogFailedDialogData() => new(localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogMessage"));

    public async Task<SettingsPageDialogData> ExportApplicationSettingsAsync(string applicationSettingsFilePath)
    {
        IsExportApplicationSettingsButtonEnabled = false;
        try
        {
            await applicationSettingsService.ExportSettingsAsync(applicationSettingsFilePath);
            return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsDialogMessage"));
        }
        catch { return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsFailedDialogMessage")); }
        finally { IsExportApplicationSettingsButtonEnabled = true; }
    }

    public async Task<SettingsPageDialogData> ImportApplicationSettingsAsync(string applicationSettingsFilePath)
    {
        var previousLanguageOverride = applicationSettings.LanguageOverride;
        IsImportApplicationSettingsButtonEnabled = false;
        try
        {
            await applicationSettingsService.ImportSettingsAsync(applicationSettingsFilePath);
            applicationThemeService.ApplyTheme(applicationSettings.Theme);
            localizationService.ApplyLanguageTag(applicationSettings.LanguageOverride);
            if (applicationSettings.HideTaskbarUsage) App.CloseTaskbarUsageWindow();
            else await App.InitializeTaskbarUsageWindowAsync();
            var isStartupLaunchApplied = await startupRegistrationService.SetStartupLaunchEnabledAsync(applicationSettings.IsStartupLaunchEnabled);
            if (!isStartupLaunchApplied) await RefreshStartupLaunchStateFromSystemAsync();

            var didLanguageOverrideChange = !string.Equals(previousLanguageOverride, applicationSettings.LanguageOverride, StringComparison.Ordinal);
            SynchronizePropertiesFromSettings();
            RefreshUsageRefreshCountdownTexts();
            return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsDialogTitle"), localizationService.GetLocalizedString(didLanguageOverrideChange ? "SettingsPage_ImportApplicationSettingsLanguageChangedDialogMessage" : "SettingsPage_ImportApplicationSettingsDialogMessage"), ShouldNavigateToSettingsAfterClose: didLanguageOverrideChange);
        }
        catch { return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsFailedDialogMessage")); }
        finally { IsImportApplicationSettingsButtonEnabled = true; }
    }

    public async Task<SettingsPageDialogData> ExportIntegratedLogAsync(string integratedLogFilePath)
    {
        IsExportIntegratedLogButtonEnabled = false;
        try
        {
            await fileLogService.ExportAsync(integratedLogFilePath);
            return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogDialogMessage"));
        }
        catch { return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogMessage")); }
        finally { IsExportIntegratedLogButtonEnabled = true; }
    }

    public async Task<SettingsPageUpdateCheckResult> CheckForUpdatesAsync()
    {
        IsCheckForUpdatesButtonEnabled = false;
        try
        {
            var availableUpdateCount = await storeUpdateService.GetAvailableUpdateCountAsync();
            if (availableUpdateCount > 0)
            {
                return new SettingsPageUpdateCheckResult(localizationService.GetLocalizedString("SettingsPage_UpdateAvailableDialogTitle"), localizationService.GetFormattedString("SettingsPage_UpdateAvailableDialogMessageFormat", availableUpdateCount, ApplicationVersionText), localizationService.GetLocalizedString("SettingsPage_OpenStoreButtonText"), localizationService.GetLocalizedString("DialogHelper_CancelButtonText"), true);
            }

            return new SettingsPageUpdateCheckResult(localizationService.GetLocalizedString("SettingsPage_NoUpdateDialogTitle"), localizationService.GetFormattedString("SettingsPage_NoUpdateDialogMessageFormat", ApplicationVersionText), null, null, false);
        }
        catch { return new SettingsPageUpdateCheckResult(localizationService.GetLocalizedString("SettingsPage_UpdateCheckFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_UpdateCheckFailedDialogMessage"), null, null, false); }
        finally { IsCheckForUpdatesButtonEnabled = true; }
    }

    public async Task OpenStoreProductPageAsync()
    {
        _ = await storeUpdateService.OpenStoreProductPageAsync();
    }

    public void RefreshUsageRefreshCountdownTexts()
    {
        ActiveAccountNextUsageRefreshText = FormatNextUsageRefreshText(accountServiceManager.GetActiveUsageRefreshRemainingTime());
        InactiveAccountNextUsageRefreshText = FormatNextUsageRefreshText(accountServiceManager.GetInactiveUsageRefreshRemainingTime());
    }

    public async Task<SettingsPageDialogData> ApplyLanguageSelectedIndexAsync(int selectedIndex)
    {
        try
        {
            var languageOverride = GetLanguageOverrideFromSelectedIndex(selectedIndex);
            if (applicationSettings.LanguageOverride == languageOverride) return null;
            LanguageSelectedIndex = selectedIndex;
            applicationSettings.LanguageOverride = languageOverride;
            localizationService.ApplyLanguageTag(languageOverride);
            if (!await SaveSettingsAsync()) return CreateSaveSettingsFailedDialogData();
            return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_LanguageRestartDialogTitle"), localizationService.GetLocalizedString("SettingsPage_LanguageRestartDialogMessage"), ShouldNavigateToSettingsAfterClose: true);
        }
        catch { return CreateSaveSettingsFailedDialogData(); }
    }

    public async Task<SettingsPageDialogData> ApplyThemeSelectedIndexAsync(int selectedIndex)
    {
        try
        {
            var theme = GetThemeFromSelectedIndex(selectedIndex);
            if (applicationSettings.Theme == theme) return null;
            ThemeSelectedIndex = selectedIndex;
            applicationSettings.Theme = theme;
            applicationThemeService.ApplyTheme(theme);
            return await SaveSettingsAsync() ? null : CreateSaveSettingsFailedDialogData();
        }
        catch { return CreateSaveSettingsFailedDialogData(); }
    }

    public async Task<SettingsPageDialogData> ApplyAutomaticUpdateCheckEnabledAsync(bool isAutomaticUpdateCheckEnabled) => await SaveSettingAsync(() =>
    {
        IsAutomaticUpdateCheckEnabled = isAutomaticUpdateCheckEnabled;
        applicationSettings.IsAutomaticUpdateCheckEnabled = isAutomaticUpdateCheckEnabled;
    });

    public async Task<SettingsPageDialogData> ApplyStartupLaunchEnabledAsync(bool isStartupLaunchEnabled)
    {
        try
        {
            IsStartupLaunchEnabled = isStartupLaunchEnabled;
            applicationSettings.IsStartupLaunchEnabled = isStartupLaunchEnabled;
            if (!await SaveSettingsAsync()) return CreateSaveSettingsFailedDialogData();

            var isStartupLaunchApplied = await startupRegistrationService.SetStartupLaunchEnabledAsync(isStartupLaunchEnabled);
            if (isStartupLaunchApplied) return null;

            return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogMessage"), ShouldRefreshStartupLaunchStateAfterClose: true);
        }
        catch { return new SettingsPageDialogData(localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogMessage"), ShouldRefreshStartupLaunchStateAfterClose: true); }
    }

    public async Task<SettingsPageDialogData> ApplyTaskbarUsageVisibleAsync(bool isTaskbarUsageVisible)
    {
        try
        {
            IsTaskbarUsageVisible = isTaskbarUsageVisible;
            applicationSettings.HideTaskbarUsage = !isTaskbarUsageVisible;
            if (!await SaveSettingsAsync()) return CreateSaveSettingsFailedDialogData();

            if (isTaskbarUsageVisible) await App.InitializeTaskbarUsageWindowAsync();
            else App.CloseTaskbarUsageWindow();

            return null;
        }
        catch { return CreateSaveSettingsFailedDialogData(); }
    }

    public async Task<SettingsPageDialogData> ApplyExpiredAccountAutomaticDeletionEnabledAsync(bool isExpiredAccountAutomaticDeletionEnabled) => await SaveSettingAsync(() =>
    {
        IsExpiredAccountAutomaticDeletionEnabled = isExpiredAccountAutomaticDeletionEnabled;
        applicationSettings.IsExpiredAccountAutomaticDeletionEnabled = isExpiredAccountAutomaticDeletionEnabled;
    });

    public async Task<SettingsPageDialogData> ApplyExpiredAccountNotificationEnabledAsync(bool isExpiredAccountNotificationEnabled) => await SaveSettingAsync(() =>
    {
        IsExpiredAccountNotificationEnabled = isExpiredAccountNotificationEnabled;
        applicationSettings.IsExpiredAccountNotificationEnabled = isExpiredAccountNotificationEnabled;
    });

    public async Task<SettingsPageDialogData> ApplyActiveAccountUsageAutomaticRefreshEnabledAsync(bool isActiveAccountUsageAutomaticRefreshEnabled)
    {
        var dialogData = await SaveSettingAsync(() =>
        {
            IsActiveAccountUsageAutomaticRefreshEnabled = isActiveAccountUsageAutomaticRefreshEnabled;
            applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled = isActiveAccountUsageAutomaticRefreshEnabled;
        });
        RefreshUsageRefreshCountdownTexts();
        return dialogData;
    }

    public async Task<SettingsPageDialogData> ApplyActiveAccountUsageRefreshIntervalMinutesAsync(double refreshIntervalMinutes) => await ApplyUsageRefreshIntervalMinutesAsync(refreshIntervalMinutes, applicationSettings.ActiveAccountUsageRefreshIntervalSeconds, refreshIntervalSeconds => applicationSettings.ActiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds, SetActiveAccountUsageRefreshIntervalMinutesSilently);

    public async Task<SettingsPageDialogData> ApplyInactiveAccountUsageAutomaticRefreshEnabledAsync(bool isInactiveAccountUsageAutomaticRefreshEnabled)
    {
        var dialogData = await SaveSettingAsync(() =>
        {
            IsInactiveAccountUsageAutomaticRefreshEnabled = isInactiveAccountUsageAutomaticRefreshEnabled;
            applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled = isInactiveAccountUsageAutomaticRefreshEnabled;
        });
        RefreshUsageRefreshCountdownTexts();
        return dialogData;
    }

    public async Task<SettingsPageDialogData> ApplyInactiveAccountUsageRefreshIntervalMinutesAsync(double refreshIntervalMinutes) => await ApplyUsageRefreshIntervalMinutesAsync(refreshIntervalMinutes, applicationSettings.InactiveAccountUsageRefreshIntervalSeconds, refreshIntervalSeconds => applicationSettings.InactiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds, SetInactiveAccountUsageRefreshIntervalMinutesSilently);

    public async Task<SettingsPageDialogData> ApplyPrimaryUsageWarningThresholdPercentageAsync(double percentage) => await ApplyPercentageAsync(percentage, applicationSettings.PrimaryUsageWarningThresholdPercentage, normalizedPercentage => applicationSettings.PrimaryUsageWarningThresholdPercentage = normalizedPercentage, SetPrimaryUsageWarningThresholdPercentageSilently);

    public async Task<SettingsPageDialogData> ApplySecondaryUsageWarningThresholdPercentageAsync(double percentage) => await ApplyPercentageAsync(percentage, applicationSettings.SecondaryUsageWarningThresholdPercentage, normalizedPercentage => applicationSettings.SecondaryUsageWarningThresholdPercentage = normalizedPercentage, SetSecondaryUsageWarningThresholdPercentageSilently);

    public async Task<SettingsPageDialogData> ApplyPrimaryUsageLowQuotaNotificationEnabledAsync(bool isPrimaryUsageLowQuotaNotificationEnabled) => await SaveSettingAsync(() =>
    {
        IsPrimaryUsageLowQuotaNotificationEnabled = isPrimaryUsageLowQuotaNotificationEnabled;
        applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled = isPrimaryUsageLowQuotaNotificationEnabled;
    });

    public async Task<SettingsPageDialogData> ApplySecondaryUsageLowQuotaNotificationEnabledAsync(bool isSecondaryUsageLowQuotaNotificationEnabled) => await SaveSettingAsync(() =>
    {
        IsSecondaryUsageLowQuotaNotificationEnabled = isSecondaryUsageLowQuotaNotificationEnabled;
        applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled = isSecondaryUsageLowQuotaNotificationEnabled;
    });

    public async Task<SettingsPageDialogData> ApplyPrimaryUsageSurgeNotificationEnabledAsync(bool isPrimaryUsageSurgeNotificationEnabled) => await SaveSettingAsync(() =>
    {
        IsPrimaryUsageSurgeNotificationEnabled = isPrimaryUsageSurgeNotificationEnabled;
        applicationSettings.IsPrimaryUsageSurgeNotificationEnabled = isPrimaryUsageSurgeNotificationEnabled;
    });

    public async Task<SettingsPageDialogData> ApplyPrimaryUsageSurgeNotificationThresholdPercentageAsync(double percentage)
        => await ApplyBoundedIntegerSettingAsync(percentage, applicationSettings.PrimaryUsageSurgeNotificationThresholdPercentage, MinimumUsageSurgeNotificationThresholdPercentage, MaximumUsageSurgeNotificationThresholdPercentage, normalizedPercentage => applicationSettings.PrimaryUsageSurgeNotificationThresholdPercentage = normalizedPercentage, SetPrimaryUsageSurgeNotificationThresholdPercentageSilently);

    public async Task<SettingsPageDialogData> ApplyPrimaryUsageSurgeNotificationWindowMinutesAsync(double minutes)
        => await ApplyBoundedIntegerSettingAsync(minutes, applicationSettings.PrimaryUsageSurgeNotificationWindowMinutes, MinimumUsageSurgeNotificationWindowMinutes, MaximumUsageSurgeNotificationWindowMinutes, normalizedMinutes => applicationSettings.PrimaryUsageSurgeNotificationWindowMinutes = normalizedMinutes, SetPrimaryUsageSurgeNotificationWindowMinutesSilently);

    private void SynchronizePropertiesFromSettings()
    {
        LanguageSelectedIndex = GetLanguageSelectedIndex(applicationSettings.LanguageOverride);
        ThemeSelectedIndex = GetThemeSelectedIndex(applicationSettings.Theme);
        IsAutomaticUpdateCheckEnabled = applicationSettings.IsAutomaticUpdateCheckEnabled;
        IsStartupLaunchEnabled = applicationSettings.IsStartupLaunchEnabled;
        IsTaskbarUsageVisible = !applicationSettings.HideTaskbarUsage;
        IsExpiredAccountAutomaticDeletionEnabled = applicationSettings.IsExpiredAccountAutomaticDeletionEnabled;
        IsExpiredAccountNotificationEnabled = applicationSettings.IsExpiredAccountNotificationEnabled;
        IsActiveAccountUsageAutomaticRefreshEnabled = applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;
        ActiveAccountUsageRefreshIntervalMinutes = ConvertSecondsToWholeMinutes(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds);
        IsInactiveAccountUsageAutomaticRefreshEnabled = applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;
        InactiveAccountUsageRefreshIntervalMinutes = ConvertSecondsToWholeMinutes(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds);
        PrimaryUsageWarningThresholdPercentage = applicationSettings.PrimaryUsageWarningThresholdPercentage;
        SecondaryUsageWarningThresholdPercentage = applicationSettings.SecondaryUsageWarningThresholdPercentage;
        IsPrimaryUsageLowQuotaNotificationEnabled = applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled;
        IsSecondaryUsageLowQuotaNotificationEnabled = applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled;
        IsPrimaryUsageSurgeNotificationEnabled = applicationSettings.IsPrimaryUsageSurgeNotificationEnabled;
        PrimaryUsageSurgeNotificationThresholdPercentage = applicationSettings.PrimaryUsageSurgeNotificationThresholdPercentage;
        PrimaryUsageSurgeNotificationWindowMinutes = applicationSettings.PrimaryUsageSurgeNotificationWindowMinutes;
    }

    public async Task RefreshStartupLaunchStateFromSystemAsync()
    {
        var isStartupLaunchEnabled = await startupRegistrationService.GetIsStartupLaunchEnabledAsync();
        applicationSettings.IsStartupLaunchEnabled = isStartupLaunchEnabled;
        SetIsStartupLaunchEnabledSilently(isStartupLaunchEnabled);
        await SaveSettingsAsync();
    }

    private async Task<SettingsPageDialogData> ApplyUsageRefreshIntervalMinutesAsync(double refreshIntervalMinutes, int fallbackRefreshIntervalSeconds, Action<int> applyRefreshIntervalSeconds, Action<double> setRefreshIntervalMinutesSilently)
    {
        var refreshIntervalSeconds = NormalizeUsageRefreshIntervalSeconds(refreshIntervalMinutes, fallbackRefreshIntervalSeconds);
        applyRefreshIntervalSeconds(refreshIntervalSeconds);
        setRefreshIntervalMinutesSilently(ConvertSecondsToWholeMinutes(refreshIntervalSeconds));
        var wasSaved = await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
        return wasSaved ? null : CreateSaveSettingsFailedDialogData();
    }

    private async Task<SettingsPageDialogData> ApplyPercentageAsync(double percentage, int fallbackPercentage, Action<int> applyPercentage, Action<double> setPercentageSilently)
    {
        var normalizedPercentage = NormalizePercentage(percentage, fallbackPercentage);
        applyPercentage(normalizedPercentage);
        setPercentageSilently(normalizedPercentage);
        return await SaveSettingsAsync() ? null : CreateSaveSettingsFailedDialogData();
    }

    private async Task<SettingsPageDialogData> ApplyBoundedIntegerSettingAsync(double value, int fallbackValue, int minimumValue, int maximumValue, Action<int> applyValue, Action<double> setValueSilently)
    {
        var normalizedValue = NormalizeBoundedInteger(value, fallbackValue, minimumValue, maximumValue);
        applyValue(normalizedValue);
        setValueSilently(normalizedValue);
        return await SaveSettingsAsync() ? null : CreateSaveSettingsFailedDialogData();
    }

    private async Task<SettingsPageDialogData> SaveSettingAsync(Action applySetting)
    {
        applySetting();
        return await SaveSettingsAsync() ? null : CreateSaveSettingsFailedDialogData();
    }

    private async Task<bool> SaveSettingsAsync()
    {
        try
        {
            await applicationSettingsService.SaveSettingsAsync();
            return true;
        }
        catch { return false; }
    }

    private SettingsPageDialogData CreateSaveSettingsFailedDialogData() => new(localizationService.GetLocalizedString("SettingsPage_SaveSettingsFailedDialogTitle"), localizationService.GetLocalizedString("SettingsPage_SaveSettingsFailedDialogMessage"));

    private void SetActiveAccountUsageRefreshIntervalMinutesSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.ActiveAccountUsageRefreshIntervalMinutes = newValue);

    private void SetInactiveAccountUsageRefreshIntervalMinutesSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.InactiveAccountUsageRefreshIntervalMinutes = newValue);

    private void SetPrimaryUsageWarningThresholdPercentageSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.PrimaryUsageWarningThresholdPercentage = newValue);

    private void SetSecondaryUsageWarningThresholdPercentageSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.SecondaryUsageWarningThresholdPercentage = newValue);

    private void SetPrimaryUsageSurgeNotificationThresholdPercentageSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.PrimaryUsageSurgeNotificationThresholdPercentage = newValue);

    private void SetPrimaryUsageSurgeNotificationWindowMinutesSilently(double value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.PrimaryUsageSurgeNotificationWindowMinutes = newValue);

    private void SetIsStartupLaunchEnabledSilently(bool value) => SetPropertySilently(value, static (viewModel, newValue) => viewModel.IsStartupLaunchEnabled = newValue);

    private void SetPropertySilently<T>(T value, Action<SettingsPageViewModel, T> applyValue)
    {
        applyValue(this, value);
    }

    private string FormatNextUsageRefreshText(TimeSpan? remainingTime) => remainingTime is null ? localizationService.GetLocalizedString("SettingsPage_RefreshRemainingDisabledText") : FormatRemainingTime(remainingTime.Value);

    private string FormatRemainingTime(TimeSpan remainingTime)
    {
        var normalizedRemainingTime = remainingTime < TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
        return localizationService.GetFormattedString("SettingsPage_RefreshRemainingFormat", (int)normalizedRemainingTime.TotalHours, normalizedRemainingTime.Minutes, normalizedRemainingTime.Seconds);
    }

    private static int GetLanguageSelectedIndex(string languageOverride) => languageOverride switch { "ko-KR" => 1, "en-US" => 2, "ja-JP" => 3, "zh-Hans" => 4, "zh-Hant" => 5, _ => 0 };

    private static string GetLanguageOverrideFromSelectedIndex(int selectedIndex) => selectedIndex switch { 1 => "ko-KR", 2 => "en-US", 3 => "ja-JP", 4 => "zh-Hans", 5 => "zh-Hant", _ => "" };

    private static int GetThemeSelectedIndex(ElementTheme theme) => theme switch { ElementTheme.Light => 1, ElementTheme.Dark => 2, _ => 0 };

    private static ElementTheme GetThemeFromSelectedIndex(int selectedIndex) => selectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };

    private static int NormalizeUsageRefreshIntervalSeconds(double refreshIntervalMinutes, int fallbackRefreshIntervalSeconds)
    {
        var fallbackRefreshIntervalMinutes = ConvertSecondsToWholeMinutes(fallbackRefreshIntervalSeconds);
        var normalizedRefreshIntervalMinutes = double.IsNaN(refreshIntervalMinutes) ? fallbackRefreshIntervalMinutes : Math.Clamp((int)Math.Round(refreshIntervalMinutes, MidpointRounding.AwayFromZero), MinimumUsageRefreshIntervalMinutes, MaximumUsageRefreshIntervalMinutes);
        return normalizedRefreshIntervalMinutes * 60;
    }

    private static int ConvertSecondsToWholeMinutes(int seconds) => Math.Clamp((int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero), MinimumUsageRefreshIntervalMinutes, MaximumUsageRefreshIntervalMinutes);

    private static int NormalizePercentage(double percentage, int fallbackPercentage) => double.IsNaN(percentage) ? fallbackPercentage : Math.Clamp((int)Math.Round(percentage, MidpointRounding.AwayFromZero), 0, 100);

    private static int NormalizeBoundedInteger(double value, int fallbackValue, int minimumValue, int maximumValue) => double.IsNaN(value) ? fallbackValue : Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), minimumValue, maximumValue);

    private static string GetCurrentApplicationVersion() => FormatCurrentApplicationVersion(Package.Current.Id.Version);

    private static string FormatCurrentApplicationVersion(PackageVersion packageVersion) => $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
}
