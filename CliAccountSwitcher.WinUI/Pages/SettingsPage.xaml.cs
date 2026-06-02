using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.Views;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private const int MinimumUsageRefreshIntervalMinutes = 1;
    private const int MaximumUsageRefreshIntervalMinutes = 1440;
    private const string ApplicationSettingsFileExtension = ".casc";
    private const string LogFileExtension = ".txt";
    private bool _isSynchronizingControls;
    private readonly DispatcherTimer _refreshCountdownDispatcherTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly ApplicationSettings _applicationSettings = App.Services.GetRequiredService<ApplicationSettings>();
    private readonly ApplicationSettingsService _applicationSettingsService = App.Services.GetRequiredService<ApplicationSettingsService>();
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly StartupRegistrationService _startupRegistrationService = App.Services.GetRequiredService<StartupRegistrationService>();
    private readonly StoreUpdateService _storeUpdateService = App.Services.GetRequiredService<StoreUpdateService>();
    private readonly FileLogService _fileLogService = App.Services.GetRequiredService<FileLogService>();
    private readonly AccountServiceManager _accountServiceManager = App.Services.GetRequiredService<AccountServiceManager>();

    public string ApplicationVersionText { get; }

    public SettingsPage()
    {
        ApplicationVersionText = _localizationService.GetFormattedString("SettingsPage_ApplicationVersionFormat", AboutPage.GetCurrentApplicationVersion());
        InitializeComponent();
    }

    private void OnSettingsPageLoaded(object sender, RoutedEventArgs routedEventArguments)
    {
        _applicationThemeService.RegisterThemeTarget(this);
        _refreshCountdownDispatcherTimer.Tick -= OnRefreshCountdownDispatcherTimerTick;
        _refreshCountdownDispatcherTimer.Tick += OnRefreshCountdownDispatcherTimerTick;
        SynchronizeControlsFromSettings();
        RefreshUsageRefreshCountdownTexts();
        _refreshCountdownDispatcherTimer.Start();
    }

    private void OnSettingsPageUnloaded(object sender, RoutedEventArgs routedEventArguments)
    {
        _refreshCountdownDispatcherTimer.Stop();
        _refreshCountdownDispatcherTimer.Tick -= OnRefreshCountdownDispatcherTimerTick;
    }

    private void SynchronizeControlsFromSettings()
    {
        _isSynchronizingControls = true;
        try
        {
            var applicationSettings = _applicationSettings;
            LanguageComboBox.SelectedIndex = GetLanguageSelectedIndex(applicationSettings.LanguageOverride);
            ThemeComboBox.SelectedIndex = GetThemeSelectedIndex(applicationSettings.Theme);
            AutomaticUpdateToggleSwitch.IsOn = applicationSettings.IsAutomaticUpdateCheckEnabled;
            StartupLaunchToggleSwitch.IsOn = applicationSettings.IsStartupLaunchEnabled;
            ExpiredAccountAutomaticDeletionToggleSwitch.IsOn = applicationSettings.IsExpiredAccountAutomaticDeletionEnabled;
            ExpiredAccountNotificationToggleSwitch.IsOn = applicationSettings.IsExpiredAccountNotificationEnabled;
            ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn = applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;
            ActiveAccountUsageRefreshIntervalNumberBox.Value = ConvertSecondsToWholeMinutes(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds);
            ActiveAccountUsageRefreshIntervalNumberBox.IsEnabled = applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;
            InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn = applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;
            InactiveAccountUsageRefreshIntervalNumberBox.Value = ConvertSecondsToWholeMinutes(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds);
            InactiveAccountUsageRefreshIntervalNumberBox.IsEnabled = applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;
            PrimaryUsageWarningThresholdNumberBox.Value = applicationSettings.PrimaryUsageWarningThresholdPercentage;
            SecondaryUsageWarningThresholdNumberBox.Value = applicationSettings.SecondaryUsageWarningThresholdPercentage;
            PrimaryUsageLowQuotaNotificationToggleSwitch.IsOn = applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled;
            SecondaryUsageLowQuotaNotificationToggleSwitch.IsOn = applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled;
        }
        finally { _isSynchronizingControls = false; }
    }

    private async void OnExportApplicationSettingsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileSavePicker = CreateApplicationSettingsFileSavePicker();
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        ExportApplicationSettingsButton.IsEnabled = false;
        try
        {
            await _applicationSettingsService.ExportSettingsAsync(storageFile.Path);
            await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsDialogMessage"));
        }
        catch { await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsFailedDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ExportApplicationSettingsFailedDialogMessage")); }
        finally { ExportApplicationSettingsButton.IsEnabled = true; }
    }

    private async void OnImportApplicationSettingsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateApplicationSettingsFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        var previousLanguageOverride = _applicationSettings.LanguageOverride;
        var wasApplicationSettingsImported = false;

        ImportApplicationSettingsButton.IsEnabled = false;
        MainWindow.ShowLoading(_localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsLoadingMessage"));
        try
        {
            await _applicationSettingsService.ImportSettingsAsync(storageFile.Path);
            _applicationThemeService.ApplyTheme(_applicationSettings.Theme);
            _localizationService.ApplyLanguageTag(_applicationSettings.LanguageOverride);
            var isStartupLaunchApplied = await _startupRegistrationService.SetStartupLaunchEnabledAsync(_applicationSettings.IsStartupLaunchEnabled);
            if (!isStartupLaunchApplied) await RefreshStartupLaunchStateFromSystemAsync();
            wasApplicationSettingsImported = true;
        }
        catch { }
        finally
        {
            MainWindow.HideLoading();
            ImportApplicationSettingsButton.IsEnabled = true;
        }

        if (!wasApplicationSettingsImported)
        {
            await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsFailedDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsFailedDialogMessage"));
            return;
        }

        var didLanguageOverrideChange = !string.Equals(previousLanguageOverride, _applicationSettings.LanguageOverride, StringComparison.Ordinal);
        SynchronizeControlsFromSettings();
        RefreshUsageRefreshCountdownTexts();
        await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ImportApplicationSettingsDialogTitle"), _localizationService.GetLocalizedString(didLanguageOverrideChange ? "SettingsPage_ImportApplicationSettingsLanguageChangedDialogMessage" : "SettingsPage_ImportApplicationSettingsDialogMessage"));
        if (didLanguageOverrideChange) WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Settings));
    }

    private async void OnExportIntegratedLogButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        ExportIntegratedLogButton.IsEnabled = false;
        try
        {
            if (!_fileLogService.HasLogs())
            {
                await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogEmptyDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogEmptyDialogMessage"));
                return;
            }

            var fileSavePicker = CreateIntegratedLogFileSavePicker();
            var storageFile = await fileSavePicker.PickSaveFileAsync();
            if (storageFile is null) return;

            await _fileLogService.ExportAsync(storageFile.Path);
            await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogDialogMessage"));
        }
        catch { await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_ExportIntegratedLogFailedDialogMessage")); }
        finally { ExportIntegratedLogButton.IsEnabled = true; }
    }

    private async void OnLanguageComboBoxSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        if (_isSynchronizingControls) return;

        var languageOverride = GetLanguageOverrideFromSelectedIndex(LanguageComboBox.SelectedIndex);
        if (_applicationSettings.LanguageOverride == languageOverride) return;

        _applicationSettings.LanguageOverride = languageOverride;
        _localizationService.ApplyLanguageTag(languageOverride);
        await SaveSettingsAsync();
        await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_LanguageRestartDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_LanguageRestartDialogMessage"));
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Settings));
    }

    private async void OnThemeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        if (_isSynchronizingControls) return;

        var theme = GetThemeFromSelectedIndex(ThemeComboBox.SelectedIndex);

        if (_applicationSettings.Theme == theme) return;

        _applicationSettings.Theme = theme;
        _applicationThemeService.ApplyTheme(theme);
        await SaveSettingsAsync();
    }

    private async void OnCheckForUpdatesButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        CheckForUpdatesButton.IsEnabled = false;
        var dialogTitle = "";
        var dialogMessage = "";
        var primaryButtonText = default(string);
        var secondaryButtonText = default(string);
        var shouldOpenStoreAfterDialog = false;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("SettingsPage_CheckForUpdatesLoadingMessage"));
        try
        {
            var availableUpdateCount = await _storeUpdateService.GetAvailableUpdateCountAsync();
            if (availableUpdateCount > 0)
            {
                dialogTitle = _localizationService.GetLocalizedString("SettingsPage_UpdateAvailableDialogTitle");
                dialogMessage = _localizationService.GetFormattedString("SettingsPage_UpdateAvailableDialogMessageFormat", availableUpdateCount, ApplicationVersionText);
                primaryButtonText = _localizationService.GetLocalizedString("SettingsPage_OpenStoreButtonText");
                secondaryButtonText = _localizationService.GetLocalizedString("DialogHelper_CancelButtonText");
                shouldOpenStoreAfterDialog = true;
            }
            else
            {
                dialogTitle = _localizationService.GetLocalizedString("SettingsPage_NoUpdateDialogTitle");
                dialogMessage = _localizationService.GetFormattedString("SettingsPage_NoUpdateDialogMessageFormat", ApplicationVersionText);
            }
        }
        catch
        {
            dialogTitle = _localizationService.GetLocalizedString("SettingsPage_UpdateCheckFailedDialogTitle");
            dialogMessage = _localizationService.GetLocalizedString("SettingsPage_UpdateCheckFailedDialogMessage");
        }
        finally
        {
            MainWindow.HideLoading();
            CheckForUpdatesButton.IsEnabled = true;
        }

        var contentDialogResult = await this.ShowDialogAsync(dialogTitle, dialogMessage, primaryButtonText, secondaryButtonText);
        if (shouldOpenStoreAfterDialog && contentDialogResult == ContentDialogResult.Primary) await _storeUpdateService.OpenStoreProductPageAsync();
    }

    private async void OnAutomaticUpdateToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsAutomaticUpdateCheckEnabled = AutomaticUpdateToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnStartupLaunchToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;

        _applicationSettings.IsStartupLaunchEnabled = StartupLaunchToggleSwitch.IsOn;
        await SaveSettingsAsync();
        var isStartupLaunchApplied = await _startupRegistrationService.SetStartupLaunchEnabledAsync(StartupLaunchToggleSwitch.IsOn);
        if (!isStartupLaunchApplied)
        {
            await this.ShowDialogAsync(_localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogTitle"), _localizationService.GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogMessage"));
            await RefreshStartupLaunchStateFromSystemAsync();
        }
    }

    private async void OnExpiredAccountAutomaticDeletionToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsExpiredAccountAutomaticDeletionEnabled = ExpiredAccountAutomaticDeletionToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnExpiredAccountNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsExpiredAccountNotificationEnabled = ExpiredAccountNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnActiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled = ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        ActiveAccountUsageRefreshIntervalNumberBox.IsEnabled = ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnActiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var refreshIntervalSeconds = NormalizeUsageRefreshIntervalSeconds(numberBoxValueChangedEventArguments.NewValue, _applicationSettings.ActiveAccountUsageRefreshIntervalSeconds);
        _applicationSettings.ActiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds;
        SetNumberBoxValue(sender, ConvertSecondsToWholeMinutes(refreshIntervalSeconds));
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnInactiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled = InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        InactiveAccountUsageRefreshIntervalNumberBox.IsEnabled = InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnInactiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var refreshIntervalSeconds = NormalizeUsageRefreshIntervalSeconds(numberBoxValueChangedEventArguments.NewValue, _applicationSettings.InactiveAccountUsageRefreshIntervalSeconds);
        _applicationSettings.InactiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds;
        SetNumberBoxValue(sender, ConvertSecondsToWholeMinutes(refreshIntervalSeconds));
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnPrimaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var warningThresholdPercentage = NormalizePercentage(numberBoxValueChangedEventArguments.NewValue, _applicationSettings.PrimaryUsageWarningThresholdPercentage);
        _applicationSettings.PrimaryUsageWarningThresholdPercentage = warningThresholdPercentage;
        SetNumberBoxValue(sender, warningThresholdPercentage);
        await SaveSettingsAsync();
    }

    private async void OnSecondaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var warningThresholdPercentage = NormalizePercentage(numberBoxValueChangedEventArguments.NewValue, _applicationSettings.SecondaryUsageWarningThresholdPercentage);
        _applicationSettings.SecondaryUsageWarningThresholdPercentage = warningThresholdPercentage;
        SetNumberBoxValue(sender, warningThresholdPercentage);
        await SaveSettingsAsync();
    }

    private async void OnPrimaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled = PrimaryUsageLowQuotaNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnSecondaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        _applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled = SecondaryUsageLowQuotaNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private void OnRefreshCountdownDispatcherTimerTick(object sender, object eventArguments) => RefreshUsageRefreshCountdownTexts();

    private async Task RefreshStartupLaunchStateFromSystemAsync()
    {
        var isStartupLaunchEnabled = await _startupRegistrationService.GetIsStartupLaunchEnabledAsync();
        _applicationSettings.IsStartupLaunchEnabled = isStartupLaunchEnabled;
        _isSynchronizingControls = true;
        try { StartupLaunchToggleSwitch.IsOn = isStartupLaunchEnabled; }
        finally { _isSynchronizingControls = false; }
        await SaveSettingsAsync();
    }

    private void RefreshUsageRefreshCountdownTexts()
    {
        ActiveAccountNextUsageRefreshTextBlock.Text = FormatNextUsageRefreshText(_accountServiceManager.GetActiveUsageRefreshRemainingTime());
        InactiveAccountNextUsageRefreshTextBlock.Text = FormatNextUsageRefreshText(_accountServiceManager.GetInactiveUsageRefreshRemainingTime());
    }

    private string FormatNextUsageRefreshText(TimeSpan? remainingTime) => remainingTime is null ? _localizationService.GetLocalizedString("SettingsPage_RefreshRemainingDisabledText") : FormatRemainingTime(remainingTime.Value);

    private string FormatRemainingTime(TimeSpan remainingTime)
    {
        var normalizedRemainingTime = remainingTime < TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
        return _localizationService.GetFormattedString("SettingsPage_RefreshRemainingFormat", (int)normalizedRemainingTime.TotalHours, normalizedRemainingTime.Minutes, normalizedRemainingTime.Seconds);
    }

    private static FileOpenPicker CreateApplicationSettingsFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileOpenPicker.FileTypeFilter.Add(ApplicationSettingsFileExtension);
        return fileOpenPicker;
    }

    private FileSavePicker CreateApplicationSettingsFileSavePicker()
    {
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"codex-account-switch-settings-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}"
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(_localizationService.GetLocalizedString("SettingsPage_ApplicationSettingsFileTypeChoice"), [ApplicationSettingsFileExtension]);
        return fileSavePicker;
    }

    private FileSavePicker CreateIntegratedLogFileSavePicker()
    {
        var fileSavePicker = new FileSavePicker
        {
            DefaultFileExtension = LogFileExtension,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"codex-account-switch-logs-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}"
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(_localizationService.GetLocalizedString("SettingsPage_IntegratedLogFileTypeChoice"), [LogFileExtension]);
        return fileSavePicker;
    }

    private static int GetLanguageSelectedIndex(string languageOverride) => languageOverride switch { "ko-KR" => 1, "en-US" => 2, "ja-JP" => 3, "zh-Hans" => 4, "zh-Hant" => 5, _ => 0  };

    private static string GetLanguageOverrideFromSelectedIndex(int selectedIndex) => selectedIndex switch { 1 => "ko-KR", 2 => "en-US", 3 => "ja-JP", 4 => "zh-Hans", 5 => "zh-Hant", _ => ""  };

    private static int GetThemeSelectedIndex(ElementTheme theme) => theme switch { ElementTheme.Light => 1, ElementTheme.Dark => 2, _ => 0  };

    private static ElementTheme GetThemeFromSelectedIndex(int selectedIndex) => selectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default  };

    private void SetNumberBoxValue(NumberBox numberBox, int value)
    {
        if (Math.Abs(numberBox.Value - value) < 0.01) return;

        _isSynchronizingControls = true;
        try { numberBox.Value = value; }
        finally { _isSynchronizingControls = false; }
    }

    private static int NormalizeUsageRefreshIntervalSeconds(double refreshIntervalMinutes, int fallbackRefreshIntervalSeconds)
    {
        var fallbackRefreshIntervalMinutes = ConvertSecondsToWholeMinutes(fallbackRefreshIntervalSeconds);
        var normalizedRefreshIntervalMinutes = double.IsNaN(refreshIntervalMinutes) ? fallbackRefreshIntervalMinutes : Math.Clamp((int)Math.Round(refreshIntervalMinutes, MidpointRounding.AwayFromZero), MinimumUsageRefreshIntervalMinutes, MaximumUsageRefreshIntervalMinutes);
        return normalizedRefreshIntervalMinutes * 60;
    }

    private static int ConvertSecondsToWholeMinutes(int seconds) => Math.Clamp((int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero), MinimumUsageRefreshIntervalMinutes, MaximumUsageRefreshIntervalMinutes);

    private static int NormalizePercentage(double percentage, int fallbackPercentage) => double.IsNaN(percentage) ? fallbackPercentage : Math.Clamp((int)Math.Round(percentage, MidpointRounding.AwayFromZero), 0, 100);

    private async Task SaveSettingsAsync() => await _applicationSettingsService.SaveSettingsAsync();


}
