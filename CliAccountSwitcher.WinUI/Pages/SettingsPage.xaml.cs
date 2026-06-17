using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private const string ApplicationSettingsFileExtension = ".casc";
    private const string LogFileExtension = ".txt";

    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly DispatcherTimer _refreshCountdownDispatcherTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isSynchronizingControls = true;

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsPageViewModel>();

        InitializeComponent();
    }

    private void OnSettingsPageLoaded(object sender, RoutedEventArgs routedEventArguments)
    {
        _applicationThemeService.RegisterThemeTarget(this);

        _refreshCountdownDispatcherTimer.Tick -= OnRefreshCountdownDispatcherTimerTick;
        _refreshCountdownDispatcherTimer.Tick += OnRefreshCountdownDispatcherTimerTick;

        _isSynchronizingControls = true;
        try { ViewModel.Load(); }
        finally { _isSynchronizingControls = false; }
        _refreshCountdownDispatcherTimer.Start();
    }

    private void OnSettingsPageUnloaded(object sender, RoutedEventArgs routedEventArguments)
    {
        _refreshCountdownDispatcherTimer.Stop();
        _refreshCountdownDispatcherTimer.Tick -= OnRefreshCountdownDispatcherTimerTick;
    }

    private async void OnExportApplicationSettingsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileSavePicker = CreateApplicationSettingsFileSavePicker();
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        var dialogData = await ViewModel.ExportApplicationSettingsAsync(storageFile.Path);
        await ShowDialogAsync(dialogData);
    }

    private async void OnImportApplicationSettingsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateApplicationSettingsFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(ViewModel.ImportApplicationSettingsLoadingMessage);
        SettingsPageDialogData dialogData;

        try { dialogData = await RunWithControlSynchronizationAsync(() => ViewModel.ImportApplicationSettingsAsync(storageFile.Path)); }
        finally { MainWindow.HideLoading(); }

        await ShowDialogAsync(dialogData);
    }

    private async void OnExportIntegratedLogButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        try
        {
            if (!ViewModel.HasIntegratedLogs())
            {
                await ShowDialogAsync(ViewModel.CreateIntegratedLogEmptyDialogData());
                return;
            }

            var fileSavePicker = CreateIntegratedLogFileSavePicker();
            var storageFile = await fileSavePicker.PickSaveFileAsync();
            if (storageFile is null) return;

            var dialogData = await ViewModel.ExportIntegratedLogAsync(storageFile.Path);
            await ShowDialogAsync(dialogData);
        }
        catch { await ShowDialogAsync(ViewModel.CreateIntegratedLogFailedDialogData()); }
    }

    private async void OnCheckForUpdatesButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        MainWindow.ShowLoading(ViewModel.CheckForUpdatesLoadingMessage);
        SettingsPageUpdateCheckResult updateCheckResult;
        try { updateCheckResult = await ViewModel.CheckForUpdatesAsync(); }
        finally { MainWindow.HideLoading(); }

        var contentDialogResult = await this.ShowDialogAsync(updateCheckResult.Title, updateCheckResult.Message, updateCheckResult.PrimaryButtonText, updateCheckResult.SecondaryButtonText);
        if (updateCheckResult.ShouldOpenStoreAfterDialog && contentDialogResult == ContentDialogResult.Primary) await ViewModel.OpenStoreProductPageAsync();
    }

    private async void OnLanguageComboBoxSelectionChanged(object sender, SelectionChangedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyLanguageSelectedIndexAsync(LanguageComboBox.SelectedIndex)));
    }

    private async void OnThemeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyThemeSelectedIndexAsync(ThemeComboBox.SelectedIndex)));
    }

    private async void OnAutomaticUpdateToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyAutomaticUpdateCheckEnabledAsync(AutomaticUpdateToggleSwitch.IsOn)));
    }

    private async void OnStartupLaunchToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyStartupLaunchEnabledAsync(StartupLaunchToggleSwitch.IsOn)));
    }

    private async void OnTaskbarUsageToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyTaskbarUsageVisibleAsync(TaskbarUsageToggleSwitch.IsOn)));
    }

    private async void OnExpiredAccountAutomaticDeletionToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyExpiredAccountAutomaticDeletionEnabledAsync(ExpiredAccountAutomaticDeletionToggleSwitch.IsOn)));
    }

    private async void OnExpiredAccountNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyExpiredAccountNotificationEnabledAsync(ExpiredAccountNotificationToggleSwitch.IsOn)));
    }

    private async void OnActiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyActiveAccountUsageAutomaticRefreshEnabledAsync(ActiveAccountUsageAutomaticRefreshToggleSwitch.IsOn)));
    }

    private async void OnActiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyActiveAccountUsageRefreshIntervalMinutesAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private async void OnInactiveAccountUsageAutomaticRefreshToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyInactiveAccountUsageAutomaticRefreshEnabledAsync(InactiveAccountUsageAutomaticRefreshToggleSwitch.IsOn)));
    }

    private async void OnInactiveAccountUsageRefreshIntervalNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyInactiveAccountUsageRefreshIntervalMinutesAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private async void OnPrimaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyPrimaryUsageWarningThresholdPercentageAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private async void OnSecondaryUsageWarningThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplySecondaryUsageWarningThresholdPercentageAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private async void OnPrimaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyPrimaryUsageLowQuotaNotificationEnabledAsync(PrimaryUsageLowQuotaNotificationToggleSwitch.IsOn)));
    }

    private async void OnSecondaryUsageLowQuotaNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplySecondaryUsageLowQuotaNotificationEnabledAsync(SecondaryUsageLowQuotaNotificationToggleSwitch.IsOn)));
    }

    private async void OnPrimaryUsageSurgeNotificationToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyPrimaryUsageSurgeNotificationEnabledAsync(PrimaryUsageSurgeNotificationToggleSwitch.IsOn)));
    }

    private async void OnPrimaryUsageSurgeNotificationThresholdNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyPrimaryUsageSurgeNotificationThresholdPercentageAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private async void OnPrimaryUsageSurgeNotificationWindowNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs numberBoxValueChangedEventArguments)
    {
        if (_isSynchronizingControls) return;
        await ShowDialogIfNeededAsync(await StartWithControlSynchronization(() => ViewModel.ApplyPrimaryUsageSurgeNotificationWindowMinutesAsync(numberBoxValueChangedEventArguments.NewValue)));
    }

    private void OnRefreshCountdownDispatcherTimerTick(object sender, object eventArguments) => ViewModel.RefreshUsageRefreshCountdownTexts();

    private Task<SettingsPageDialogData> StartWithControlSynchronization(Func<Task<SettingsPageDialogData>> action)
    {
        _isSynchronizingControls = true;
        try { return action(); }
        finally { _isSynchronizingControls = false; }
    }

    private async Task<SettingsPageDialogData> RunWithControlSynchronizationAsync(Func<Task<SettingsPageDialogData>> action)
    {
        _isSynchronizingControls = true;
        try { return await action(); }
        finally { _isSynchronizingControls = false; }
    }

    private async Task RunWithControlSynchronizationAsync(Func<Task> action)
    {
        _isSynchronizingControls = true;
        try { await action(); }
        finally { _isSynchronizingControls = false; }
    }

    private async Task ShowDialogIfNeededAsync(SettingsPageDialogData dialogData)
    {
        if (dialogData is null) return;
        await ShowDialogAsync(dialogData);
    }

    private async Task ShowDialogAsync(SettingsPageDialogData dialogData)
    {
        await this.ShowDialogAsync(dialogData.Title, dialogData.Message, dialogData.PrimaryButtonText, dialogData.SecondaryButtonText);
        if (dialogData.ShouldRefreshStartupLaunchStateAfterClose) await RunWithControlSynchronizationAsync(ViewModel.RefreshStartupLaunchStateFromSystemAsync);
        if (dialogData.ShouldNavigateToSettingsAfterClose) WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Settings));
    }

    private FileOpenPicker CreateApplicationSettingsFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileOpenPicker.FileTypeFilter.Add(ApplicationSettingsFileExtension);
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        return fileOpenPicker;
    }

    private FileSavePicker CreateApplicationSettingsFileSavePicker()
    {
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileSavePicker.FileTypeChoices.Add(ViewModel.ApplicationSettingsFileTypeChoiceText, [ApplicationSettingsFileExtension]);
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        fileSavePicker.SuggestedFileName = ViewModel.ApplicationSettingsSuggestedFileName;
        return fileSavePicker;
    }

    private FileSavePicker CreateIntegratedLogFileSavePicker()
    {
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileSavePicker.FileTypeChoices.Add(ViewModel.IntegratedLogFileTypeChoiceText, [LogFileExtension]);
        fileSavePicker.DefaultFileExtension = LogFileExtension;
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        fileSavePicker.SuggestedFileName = ViewModel.IntegratedLogSuggestedFileName;
        return fileSavePicker;
    }
}
