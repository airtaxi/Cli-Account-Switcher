using CodexAccountSwitch.WinUI.Helpers;
using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Views;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private const int MinimumUsageRefreshIntervalMinutes = 1;
    private const int MaximumUsageRefreshIntervalMinutes = 1440;
    private bool _isSynchronizingControls;
    private readonly DispatcherTimer _refreshCountdownDispatcherTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public string ApplicationVersionText { get; } = GetFormattedString("SettingsPage_ApplicationVersionFormat", AboutPage.GetCurrentApplicationVersion());

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnSettingsPageLoaded(object sender, RoutedEventArgs routedEventArguments)
    {
        App.ApplicationThemeService.RegisterThemeTarget(this);
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
            var applicationSettings = App.ApplicationSettings;
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
        finally
        {
            _isSynchronizingControls = false;
        }
    }

    private async void OnLanguageComboBoxSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        if (_isSynchronizingControls) return;

        var languageOverride = ReadSelectedComboBoxTag(LanguageComboBox);
        if (App.ApplicationSettings.LanguageOverride == languageOverride) return;

        App.ApplicationSettings.LanguageOverride = languageOverride;
        App.LocalizationService.ApplyLanguageTag(languageOverride);
        await SaveSettingsAsync();
        await this.ShowDialogAsync(GetLocalizedString("SettingsPage_LanguageRestartDialogTitle"), GetLocalizedString("SettingsPage_LanguageRestartDialogMessage"));
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Settings));
    }

    private async void OnThemeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        if (_isSynchronizingControls) return;

        var theme = ReadSelectedComboBoxTag(ThemeComboBox) switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (App.ApplicationSettings.Theme == theme) return;

        App.ApplicationSettings.Theme = theme;
        App.ApplicationThemeService.ApplyTheme(theme);
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

        MainWindow.ShowLoading(GetLocalizedString("SettingsPage_CheckForUpdatesLoadingMessage"));
        try
        {
            var availableUpdateCount = await App.StoreUpdateService.GetAvailableUpdateCountAsync();
            if (availableUpdateCount > 0)
            {
                dialogTitle = GetLocalizedString("SettingsPage_UpdateAvailableDialogTitle");
                dialogMessage = GetFormattedString("SettingsPage_UpdateAvailableDialogMessageFormat", availableUpdateCount, ApplicationVersionText);
                primaryButtonText = GetLocalizedString("SettingsPage_OpenStoreButtonText");
                secondaryButtonText = GetLocalizedString("DialogHelper_CancelButtonText");
                shouldOpenStoreAfterDialog = true;
            }
            else
            {
                dialogTitle = GetLocalizedString("SettingsPage_NoUpdateDialogTitle");
                dialogMessage = GetFormattedString("SettingsPage_NoUpdateDialogMessageFormat", ApplicationVersionText);
            }
        }
        catch
        {
            dialogTitle = GetLocalizedString("SettingsPage_UpdateCheckFailedDialogTitle");
            dialogMessage = GetLocalizedString("SettingsPage_UpdateCheckFailedDialogMessage");
        }
        finally
        {
            MainWindow.HideLoading();
            CheckForUpdatesButton.IsEnabled = true;
        }

        var contentDialogResult = await this.ShowDialogAsync(dialogTitle, dialogMessage, primaryButtonText, secondaryButtonText);
        if (shouldOpenStoreAfterDialog && contentDialogResult == ContentDialogResult.Primary) await App.StoreUpdateService.OpenStoreProductPageAsync();
    }

    private async void OnAutomaticUpdateToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsAutomaticUpdateCheckEnabled = AutomaticUpdateToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnStartupLaunchToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;

        App.ApplicationSettings.IsStartupLaunchEnabled = StartupLaunchToggleSwitch.IsOn;
        await SaveSettingsAsync();
        var isStartupLaunchApplied = await App.StartupRegistrationService.SetStartupLaunchEnabledAsync(StartupLaunchToggleSwitch.IsOn);
        if (!isStartupLaunchApplied)
        {
            await this.ShowDialogAsync(GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogTitle"), GetLocalizedString("SettingsPage_StartupRegistrationFailedDialogMessage"));
            await RefreshStartupLaunchStateFromSystemAsync();
        }
    }

    private async void OnExpiredAccountAutomaticDeletionToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsExpiredAccountAutomaticDeletionEnabled = ExpiredAccountAutomaticDeletionToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnExpiredAccountNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsExpiredAccountNotificationEnabled = ExpiredAccountNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnActiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled = ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        ActiveAccountUsageRefreshIntervalNumberBox.IsEnabled = ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnActiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var refreshIntervalSeconds = NormalizeUsageRefreshIntervalSeconds(numberBoxValueChangedEventArguments.NewValue, App.ApplicationSettings.ActiveAccountUsageRefreshIntervalSeconds);
        App.ApplicationSettings.ActiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds;
        SetNumberBoxValue(sender, ConvertSecondsToWholeMinutes(refreshIntervalSeconds));
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnInactiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled = InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        InactiveAccountUsageRefreshIntervalNumberBox.IsEnabled = InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn;
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnInactiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var refreshIntervalSeconds = NormalizeUsageRefreshIntervalSeconds(numberBoxValueChangedEventArguments.NewValue, App.ApplicationSettings.InactiveAccountUsageRefreshIntervalSeconds);
        App.ApplicationSettings.InactiveAccountUsageRefreshIntervalSeconds = refreshIntervalSeconds;
        SetNumberBoxValue(sender, ConvertSecondsToWholeMinutes(refreshIntervalSeconds));
        await SaveSettingsAsync();
        RefreshUsageRefreshCountdownTexts();
    }

    private async void OnPrimaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var warningThresholdPercentage = NormalizePercentage(numberBoxValueChangedEventArguments.NewValue, App.ApplicationSettings.PrimaryUsageWarningThresholdPercentage);
        App.ApplicationSettings.PrimaryUsageWarningThresholdPercentage = warningThresholdPercentage;
        SetNumberBoxValue(sender, warningThresholdPercentage);
        await SaveSettingsAsync();
    }

    private async void OnSecondaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        var warningThresholdPercentage = NormalizePercentage(numberBoxValueChangedEventArguments.NewValue, App.ApplicationSettings.SecondaryUsageWarningThresholdPercentage);
        App.ApplicationSettings.SecondaryUsageWarningThresholdPercentage = warningThresholdPercentage;
        SetNumberBoxValue(sender, warningThresholdPercentage);
        await SaveSettingsAsync();
    }

    private async void OnPrimaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled = PrimaryUsageLowQuotaNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private async void OnSecondaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        App.ApplicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled = SecondaryUsageLowQuotaNotificationToggleSwitch.IsOn;
        await SaveSettingsAsync();
    }

    private void OnRefreshCountdownDispatcherTimerTick(object sender, object eventArguments) => RefreshUsageRefreshCountdownTexts();

    private async Task RefreshStartupLaunchStateFromSystemAsync()
    {
        var isStartupLaunchEnabled = await App.StartupRegistrationService.GetIsStartupLaunchEnabledAsync();
        App.ApplicationSettings.IsStartupLaunchEnabled = isStartupLaunchEnabled;
        _isSynchronizingControls = true;
        try { StartupLaunchToggleSwitch.IsOn = isStartupLaunchEnabled; }
        finally { _isSynchronizingControls = false; }
        await SaveSettingsAsync();
    }

    private void RefreshUsageRefreshCountdownTexts()
    {
        ActiveAccountNextUsageRefreshTextBlock.Text = FormatNextUsageRefreshText(App.CodexAccountService.GetActiveUsageRefreshRemainingTime());
        InactiveAccountNextUsageRefreshTextBlock.Text = FormatNextUsageRefreshText(App.CodexAccountService.GetInactiveUsageRefreshRemainingTime());
    }

    private static string FormatNextUsageRefreshText(TimeSpan? remainingTime) => remainingTime is null ? GetLocalizedString("SettingsPage_RefreshRemainingDisabledText") : FormatRemainingTime(remainingTime.Value);

    private static string FormatRemainingTime(TimeSpan remainingTime)
    {
        var normalizedRemainingTime = remainingTime < TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
        return GetFormattedString("SettingsPage_RefreshRemainingFormat", (int)normalizedRemainingTime.TotalHours, normalizedRemainingTime.Minutes, normalizedRemainingTime.Seconds);
    }

    private static string ReadSelectedComboBoxTag(ComboBox comboBox) => comboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "";

    private static int GetLanguageSelectedIndex(string languageOverride) => languageOverride switch
    {
        "ko-KR" => 1,
        "en-US" => 2,
        "ja-JP" => 3,
        "zh-Hans" => 4,
        "zh-Hant" => 5,
        _ => 0
    };

    private static int GetThemeSelectedIndex(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => 1,
        ElementTheme.Dark => 2,
        _ => 0
    };

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

    private static async Task SaveSettingsAsync() => await App.ApplicationSettingsService.SaveSettingsAsync();

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
