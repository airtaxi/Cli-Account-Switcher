using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class AccountsPage : Page
{
    private DispatcherTimer _remainingTimeRefreshTimer;

    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly CodexApplicationRestartService _codexApplicationRestartService = App.Services.GetRequiredService<CodexApplicationRestartService>();
    private readonly ClaudeCodeApplicationRestartService _claudeCodeApplicationRestartService = App.Services.GetRequiredService<ClaudeCodeApplicationRestartService>();

    public AccountsPageViewModel ViewModel { get; }

    public AccountsPage()
    {
        ViewModel = App.Services.GetRequiredService<AccountsPageViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(ViewModel.RefreshAllAccountsLoadingMessage, ViewModel.RefreshAllAccountsAsync);

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddAccountDialog
        {
            XamlRoot = XamlRoot
        };
        await addAccountDialog.ShowAsync();
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (!ViewModel.HasSelectedAccounts) return;

        await RunWithLoadingAsync(ViewModel.RefreshSelectedAccountsLoadingMessage, ViewModel.RefreshSelectedAccountsAsync);
    }

    private async void OnDeleteSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (!ViewModel.HasSelectedAccounts) return;

        var contentDialogResult = await ShowDialogAsync(ViewModel.CreateDeleteSelectedAccountsConfirmationDialogData());
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await ViewModel.DeleteSelectedAccountsAsync();
    }

    private async void OnExportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = ViewModel.SelectedProviderKind;
        var fileSavePicker = CreateBackupFileSavePicker(selectedProviderKind);
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        var dialogData = await ViewModel.ExportBackupAsync(selectedProviderKind, storageFile.Path);
        await ShowDialogAsync(dialogData);
    }

    private async void OnImportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = ViewModel.SelectedProviderKind;
        var fileOpenPicker = CreateBackupFileOpenPicker(selectedProviderKind);
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(ViewModel.ImportBackupLoadingMessage);
        BasicDialogData dialogData;
        try { dialogData = await ViewModel.ImportBackupAsync(selectedProviderKind, storageFile.Path); }
        finally { MainWindow.HideLoading(); }

        await ShowDialogAsync(dialogData);
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await ShowDialogAsync(ViewModel.CreateDeleteExpiredAccountsConfirmationDialogData());
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var dialogData = await ViewModel.DeleteExpiredAccountsAsync();
        await ShowDialogAsync(dialogData);
    }

    private async void OnSwitchAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var providerActivationFollowUp = await ViewModel.ActivateAccountAsync(accountIdentifier);
        await HandleProviderActivationFollowUpAsync(providerActivationFollowUp);
    }

    private async void OnRenameAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;
        if (!ViewModel.TryGetAccountCustomAlias(accountIdentifier, out var currentCustomAlias)) return;

        var customAlias = await this.ShowInputDialogAsync(ViewModel.RenameAccountDialogTitle, ViewModel.RenameAccountPlaceholderText, true, defaultText: currentCustomAlias);
        if (customAlias is null) return;
        await ViewModel.RenameAccountAsync(accountIdentifier, customAlias);
    }

    private async void OnRefreshAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await RunWithLoadingAsync(ViewModel.RefreshAccountLoadingMessage, async () => await ViewModel.RefreshAccountAsync(accountIdentifier));
    }

    private async void OnDeleteAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var contentDialogResult = await ShowDialogAsync(ViewModel.CreateDeleteAccountConfirmationDialogData());
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await ViewModel.DeleteAccountAsync(accountIdentifier);
    }

    private void OnPlanFilterSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => ViewModel.SelectedPlanFilter = sender.SelectedItem?.Tag as string ?? "All";

    private void OnSelectAllAccountsCheckBoxChecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredAccountsSelection(true);

    private void OnSelectAllAccountsCheckBoxUnchecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredAccountsSelection(false);

    private void OnAccountsPageLoaded(object sender, RoutedEventArgs routedEventArguments) => StartRemainingTimeRefreshTimer();

    private void OnAccountsPageUnloaded(object sender, RoutedEventArgs routedEventArguments)
    {
        StopRemainingTimeRefreshTimer();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(AccountsPageViewModel.SelectedProviderKind) and not nameof(AccountsPageViewModel.SelectedPlanFilter)) return;
        ResetPlanFilterSelectorBars();
    }

    private void ResetPlanFilterSelectorBars()
    {
        if (!string.Equals(ViewModel.SelectedPlanFilter, "All", StringComparison.OrdinalIgnoreCase)) return;

        if (CodexPlanFilterSelectorBar.SelectedItem != CodexAllPlanFilterSelectorBarItem) CodexPlanFilterSelectorBar.SelectedItem = CodexAllPlanFilterSelectorBarItem;
        if (ClaudeCodePlanFilterSelectorBar.SelectedItem != ClaudeCodeAllPlanFilterSelectorBarItem) ClaudeCodePlanFilterSelectorBar.SelectedItem = ClaudeCodeAllPlanFilterSelectorBarItem;
    }

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action)
    {
        MainWindow.ShowLoading(loadingMessage);
        try { await action(); }
        finally { MainWindow.HideLoading(); }
    }

    private void OnRemainingTimeRefreshTimerTick(object sender, object eventArguments) => ViewModel.RefreshUsageResetTextProperties();

    private void StartRemainingTimeRefreshTimer()
    {
        StopRemainingTimeRefreshTimer();

        _remainingTimeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _remainingTimeRefreshTimer.Tick += OnRemainingTimeRefreshTimerTick;
        _remainingTimeRefreshTimer.Start();
        ViewModel.RefreshUsageResetTextProperties();
    }

    private void StopRemainingTimeRefreshTimer()
    {
        if (_remainingTimeRefreshTimer is null) return;

        _remainingTimeRefreshTimer.Stop();
        _remainingTimeRefreshTimer.Tick -= OnRemainingTimeRefreshTimerTick;
        _remainingTimeRefreshTimer = null;
    }

    private async Task HandleProviderActivationFollowUpAsync(ProviderActivationFollowUp providerActivationFollowUp)
    {
        switch (providerActivationFollowUp)
        {
            case ProviderActivationFollowUp.RestartCodex:
                await AskToRestartCodexApplicationAsync();
                break;

            case ProviderActivationFollowUp.RestartClaudeCode:
                await AskToRestartClaudeCodeAsync();
                break;
        }
    }

    private async Task AskToRestartCodexApplicationAsync()
    {
        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationLoadingMessage"));
        var wasCodexApplicationRestarted = false;
        try { wasCodexApplicationRestarted = await _codexApplicationRestartService.RestartCodexApplicationAsync(); }
        finally { MainWindow.HideLoading(); }

        if (!wasCodexApplicationRestarted) await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationFailedDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_RestartCodexApplicationFailedDialogMessage"));
    }

    private async Task AskToRestartClaudeCodeAsync()
    {
        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionLoadingMessage"));
        var wasClaudeCodeRestarted = false;
        try { wasClaudeCodeRestarted = await _claudeCodeApplicationRestartService.RestartClaudeCodeAsync(); }
        finally { MainWindow.HideLoading(); }

        if (!wasClaudeCodeRestarted) await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionFailedDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_RestartClaudeCodeSessionFailedDialogMessage"));
    }

    private async Task<ContentDialogResult> ShowDialogAsync(BasicDialogData dialogData) => await this.ShowDialogAsync(dialogData.Title, dialogData.Message, dialogData.PrimaryButtonText, dialogData.SecondaryButtonText);

    private FileOpenPicker CreateBackupFileOpenPicker(CliProviderKind providerKind)
    {
        var backupFileExtension = AccountsPageViewModel.GetBackupFileExtension(providerKind);
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileOpenPicker.FileTypeFilter.Add(backupFileExtension);
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        return fileOpenPicker;
    }

    private FileSavePicker CreateBackupFileSavePicker(CliProviderKind providerKind)
    {
        var backupFileExtension = AccountsPageViewModel.GetBackupFileExtension(providerKind);
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileSavePicker.FileTypeChoices.Add(ViewModel.GetBackupFileTypeChoiceText(providerKind), [backupFileExtension]);
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        fileSavePicker.SuggestedFileName = ViewModel.GetBackupSuggestedFileName(providerKind);
        fileSavePicker.DefaultFileExtension = backupFileExtension;
        return fileSavePicker;
    }

    private static string ReadCommandParameter(object sender) => sender is FrameworkElement { Tag: string accountIdentifier } ? accountIdentifier : sender is Button { CommandParameter: string buttonAccountIdentifier } ? buttonAccountIdentifier : "";


}
