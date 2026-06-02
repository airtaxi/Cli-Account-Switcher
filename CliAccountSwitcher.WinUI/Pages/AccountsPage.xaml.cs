using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class AccountsPage : Page
{
    private DispatcherTimer _remainingTimeRefreshTimer;

    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly AccountServiceManager _accountServiceManager = App.Services.GetRequiredService<AccountServiceManager>();
    private readonly CodexApplicationRestartService _codexApplicationRestartService = App.Services.GetRequiredService<CodexApplicationRestartService>();
    private readonly ClaudeCodeApplicationRestartService _claudeCodeApplicationRestartService = App.Services.GetRequiredService<ClaudeCodeApplicationRestartService>();

    public AccountsPageViewModel ViewModel { get; }

    public AccountsPage()
    {
        ViewModel = App.Services.GetRequiredService<AccountsPageViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private CliProviderKind SelectedProviderKind => ViewModel.SelectedProviderKind;

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(_localizationService.GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await _accountServiceManager.RefreshAllAccountsAsync(SelectedProviderKind));

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddAccountDialog
        {
            XamlRoot = XamlRoot
        };
        await addAccountDialog.ShowAsync();
        await ViewModel.ReloadAccountsAsync();
    }

    private async void OnRefreshSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;

        await RunWithLoadingAsync(_localizationService.GetLocalizedString("AccountsPage_RefreshSelectedAccountsLoadingMessage"), async () => await _accountServiceManager.RefreshAccountsAsync(SelectedProviderKind, selectedAccountIdentifiers));
    }

    private async void OnDeleteSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;

        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteSelectedAccountsDialogTitle"), _localizationService.GetFormattedString("AccountsPage_DeleteSelectedAccountsDialogMessage", selectedAccountIdentifiers.Count), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await _accountServiceManager.DeleteAccountsAsync(SelectedProviderKind, selectedAccountIdentifiers);
        await ViewModel.ReloadAccountsAsync();
    }

    private async void OnExportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = SelectedProviderKind;
        var fileSavePicker = CreateBackupFileSavePicker(selectedProviderKind, _accountServiceManager.GetBackupFileNamePrefix(selectedProviderKind));
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        await _accountServiceManager.ExportBackupAsync(selectedProviderKind, storageFile.Path);
        await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_ExportBackupDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_ExportBackupDialogMessage"));
    }

    private async void OnImportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = SelectedProviderKind;
        var fileOpenPicker = CreateBackupFileOpenPicker(selectedProviderKind);
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("AccountsPage_ImportBackupLoadingMessage"));
        var providerAccountBackupImportResult = default(ProviderAccountBackupImportResult);
        try
        {
            providerAccountBackupImportResult = await _accountServiceManager.ImportBackupAsync(selectedProviderKind, storageFile.Path);
            await ViewModel.ReloadAccountsAsync();
        }
        catch { providerAccountBackupImportResult = new ProviderAccountBackupImportResult { FailureCount = 1 }; }
        finally { MainWindow.HideLoading(); }

        await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_ImportBackupDialogTitle"), BuildBackupImportResultText(providerAccountBackupImportResult.SuccessCount, providerAccountBackupImportResult.FailureCount, providerAccountBackupImportResult.DuplicateCount));
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedCount = await _accountServiceManager.DeleteExpiredAccountsAsync(SelectedProviderKind);
        await ViewModel.ReloadAccountsAsync();
        await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedCount == 0 ? _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : _localizationService.GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedCount));
    }

    private async void OnSwitchAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var providerActivationFollowUp = await _accountServiceManager.ActivateAccountAsync(SelectedProviderKind, accountIdentifier);
        await ViewModel.ReloadAccountsAsync();
        await HandleProviderActivationFollowUpAsync(providerActivationFollowUp);
    }

    private async void OnRenameAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;
        if (!_accountServiceManager.GetIsRenameSupported(SelectedProviderKind)) return;

        var currentAccountViewModel = ViewModel.Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
        if (currentAccountViewModel is null) return;

        var customAlias = await this.ShowInputDialogAsync(_localizationService.GetLocalizedString("AccountsPage_RenameAccountDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_RenameAccountPlaceholderText"), true, defaultText: currentAccountViewModel.CustomAlias);
        if (customAlias is null) return;
        await _accountServiceManager.RenameAccountAsync(SelectedProviderKind, accountIdentifier, customAlias);
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await RunWithLoadingAsync(_localizationService.GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage"), async () => await _accountServiceManager.RefreshAccountAsync(SelectedProviderKind, accountIdentifier));
    }

    private async void OnDeleteAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteAccountDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteAccountDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await _accountServiceManager.DeleteAccountsAsync(SelectedProviderKind, [accountIdentifier]);
        await ViewModel.ReloadAccountsAsync();
    }

    private void OnAccountSearchAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs autoSuggestBoxTextChangedEventArguments) => ViewModel.SearchText = sender.Text;

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

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action, bool shouldReloadAccounts = true)
    {
        MainWindow.ShowLoading(loadingMessage);
        try
        {
            await action();
            if (shouldReloadAccounts) await ViewModel.ReloadAccountsAsync();
        }
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

    private static FileOpenPicker CreateBackupFileOpenPicker(CliProviderKind providerKind)
    {
        var backupFileExtension = GetBackupFileExtension(providerKind);
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileOpenPicker.FileTypeFilter.Add(backupFileExtension);
        return fileOpenPicker;
    }

    private FileSavePicker CreateBackupFileSavePicker(CliProviderKind providerKind, string backupFileNamePrefix)
    {
        var backupFileExtension = GetBackupFileExtension(providerKind);
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{backupFileNamePrefix}-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}",
            DefaultFileExtension = backupFileExtension
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(_localizationService.GetLocalizedString(GetBackupFileTypeChoiceResourceName(providerKind)), [backupFileExtension]);
        return fileSavePicker;
    }

    private static string GetBackupFileExtension(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => ".ccb", _ => ".zip"  };

    private static string GetBackupFileTypeChoiceResourceName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => "AccountsPage_ClaudeCodeBackupFileTypeChoice", _ => "AccountsPage_ZipBackupFileTypeChoice"  };

    private static string ReadCommandParameter(object sender) => sender is FrameworkElement { Tag: string accountIdentifier } ? accountIdentifier : sender is Button { CommandParameter: string buttonAccountIdentifier } ? buttonAccountIdentifier : "";

    private string BuildBackupImportResultText(int successCount, int failureCount, int duplicateCount)
    {
        var resultLines = new[]
        {
            successCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupSuccessCountFormat", successCount) : "",
            failureCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupFailureCountFormat", failureCount) : "",
            duplicateCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupDuplicateCountFormat", duplicateCount) : ""
        }.Where(resultLine => !string.IsNullOrWhiteSpace(resultLine)).ToArray();

        return resultLines.Length == 0 ? _localizationService.GetLocalizedString("AccountsPage_ImportBackupEmptyResultMessage") : string.Join(Environment.NewLine, resultLines);
    }


}
