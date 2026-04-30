using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class AccountsPage : Page
{
    public AccountsPageViewModel ViewModel { get; }

    public AccountsPage()
    {
        ViewModel = new AccountsPageViewModel(App.CodexAccountService, App.ApplicationSettings, DispatcherQueue);
        InitializeComponent();
    }

    private IProviderAccountActions CurrentProviderAccountActions => App.GetProviderAccountActions(ViewModel.SelectedProviderKind);

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await CurrentProviderAccountActions.RefreshAccountsPageAllAsync());

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddAccountDialog(App.CodexAccountService)
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

        await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshSelectedAccountsLoadingMessage"), async () => await CurrentProviderAccountActions.RefreshAccountsPageSelectionAsync(selectedAccountIdentifiers));
    }

    private async void OnDeleteSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;

        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteSelectedAccountsDialogTitle"), GetFormattedString("AccountsPage_DeleteSelectedAccountsDialogMessage", selectedAccountIdentifiers.Count), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await CurrentProviderAccountActions.DeleteAccountsAsync(selectedAccountIdentifiers);
        await ViewModel.ReloadAccountsAsync();
    }

    private async void OnExportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var providerAccountActions = CurrentProviderAccountActions;
        var fileSavePicker = CreateBackupFileSavePicker(providerAccountActions);
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        await providerAccountActions.ExportBackupAsync(storageFile.Path);
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_ExportBackupDialogTitle"), GetLocalizedString("AccountsPage_ExportBackupDialogMessage"));
    }

    private async void OnImportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var providerAccountActions = CurrentProviderAccountActions;
        var fileOpenPicker = CreateBackupFileOpenPicker(providerAccountActions.ProviderKind);
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(GetLocalizedString("AccountsPage_ImportBackupLoadingMessage"));
        var providerAccountBackupImportResult = default(ProviderAccountBackupImportResult);
        try
        {
            providerAccountBackupImportResult = await providerAccountActions.ImportBackupAsync(storageFile.Path);
            await ViewModel.ReloadAccountsAsync();
        }
        catch { providerAccountBackupImportResult = new ProviderAccountBackupImportResult { FailureCount = 1 }; }
        finally
        {
            MainWindow.HideLoading();
        }

        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_ImportBackupDialogTitle"), BuildBackupImportResultText(providerAccountBackupImportResult.SuccessCount, providerAccountBackupImportResult.FailureCount, providerAccountBackupImportResult.DuplicateCount));
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedCount = await CurrentProviderAccountActions.DeleteExpiredAccountsAsync();
        await ViewModel.ReloadAccountsAsync();
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedCount == 0 ? GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedCount));
    }

    private async void OnSwitchAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var providerActivationFollowUp = await CurrentProviderAccountActions.ActivateAccountAsync(accountIdentifier);
        await ViewModel.ReloadAccountsAsync();
        await HandleProviderActivationFollowUpAsync(providerActivationFollowUp);
    }

    private async void OnRenameAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;
        if (ViewModel.SelectedProviderKind != CliProviderKind.Codex) return;

        var currentAccountViewModel = ViewModel.Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
        if (currentAccountViewModel is null) return;

        var customAlias = await this.ShowInputDialogAsync(GetLocalizedString("AccountsPage_RenameAccountDialogTitle"), GetLocalizedString("AccountsPage_RenameAccountPlaceholderText"), true, defaultText: currentAccountViewModel.CustomAlias);
        if (customAlias is null) return;
        await App.CodexAccountService.RenameAccountAsync(accountIdentifier, customAlias);
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage"), async () => await CurrentProviderAccountActions.RefreshAccountAsync(accountIdentifier));
    }

    private async void OnDeleteAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteAccountDialogTitle"), GetLocalizedString("AccountsPage_DeleteAccountDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await CurrentProviderAccountActions.DeleteAccountsAsync([accountIdentifier]);
        await ViewModel.ReloadAccountsAsync();
    }

    private void OnAccountSearchAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs autoSuggestBoxTextChangedEventArguments) => ViewModel.SearchText = sender.Text;

    private void OnPlanFilterSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => ViewModel.SelectedPlanFilter = sender.SelectedItem?.Tag as string ?? "All";

    private void OnSelectAllAccountsCheckBoxChecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredAccountsSelection(true);

    private void OnSelectAllAccountsCheckBoxUnchecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredAccountsSelection(false);

    private void OnAccountsPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action, bool shouldReloadAccounts = true)
    {
        MainWindow.ShowLoading(loadingMessage);
        try
        {
            await action();
            if (shouldReloadAccounts) await ViewModel.ReloadAccountsAsync();
        }
        finally
        {
            MainWindow.HideLoading();
        }
    }

    private async Task ShowClaudeCodeSessionRefreshGuideAsync() => await this.ShowDialogAsync(GetLocalizedString("AccountsPage_RestartClaudeCodeSessionDialogTitle"), GetLocalizedString("AccountsPage_RestartClaudeCodeSessionDialogMessage"));

    private async Task HandleProviderActivationFollowUpAsync(ProviderActivationFollowUp providerActivationFollowUp)
    {
        switch (providerActivationFollowUp)
        {
            case ProviderActivationFollowUp.RestartCodex:
                await AskToRestartCodexApplicationAsync();
                break;

            case ProviderActivationFollowUp.RefreshClaudeCodeSession:
                await ShowClaudeCodeSessionRefreshGuideAsync();
                break;
        }
    }

    private async Task AskToRestartCodexApplicationAsync()
    {
        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_RestartCodexApplicationDialogTitle"), GetLocalizedString("AccountsPage_RestartCodexApplicationDialogMessage"), GetLocalizedString("AccountsPage_RestartCodexApplicationButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        MainWindow.ShowLoading(GetLocalizedString("AccountsPage_RestartCodexApplicationLoadingMessage"));
        var wasCodexApplicationRestarted = false;
        try { wasCodexApplicationRestarted = await App.CodexApplicationRestartService.RestartCodexApplicationAsync(); }
        finally { MainWindow.HideLoading(); }

        if (!wasCodexApplicationRestarted) await this.ShowDialogAsync(GetLocalizedString("AccountsPage_RestartCodexApplicationFailedDialogTitle"), GetLocalizedString("AccountsPage_RestartCodexApplicationFailedDialogMessage"));
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

    private static FileSavePicker CreateBackupFileSavePicker(IProviderAccountActions providerAccountActions)
    {
        var backupFileExtension = GetBackupFileExtension(providerAccountActions.ProviderKind);
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{providerAccountActions.BackupFileNamePrefix}-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}",
            DefaultFileExtension = backupFileExtension
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(GetLocalizedString(GetBackupFileTypeChoiceResourceName(providerAccountActions.ProviderKind)), [backupFileExtension]);
        return fileSavePicker;
    }

    private static string GetBackupFileExtension(CliProviderKind providerKind)
        => providerKind switch
        {
            CliProviderKind.ClaudeCode => ".ccb",
            _ => ".zip"
        };

    private static string GetBackupFileTypeChoiceResourceName(CliProviderKind providerKind)
        => providerKind switch
        {
            CliProviderKind.ClaudeCode => "AccountsPage_ClaudeCodeBackupFileTypeChoice",
            _ => "AccountsPage_ZipBackupFileTypeChoice"
        };

    private static string ReadCommandParameter(object sender) => sender is FrameworkElement { Tag: string accountIdentifier } ? accountIdentifier : sender is Button { CommandParameter: string buttonAccountIdentifier } ? buttonAccountIdentifier : "";

    private static string BuildBackupImportResultText(int successCount, int failureCount, int duplicateCount)
    {
        var resultLines = new[]
        {
            successCount > 0 ? GetFormattedString("AccountsPage_ImportBackupSuccessCountFormat", successCount) : "",
            failureCount > 0 ? GetFormattedString("AccountsPage_ImportBackupFailureCountFormat", failureCount) : "",
            duplicateCount > 0 ? GetFormattedString("AccountsPage_ImportBackupDuplicateCountFormat", duplicateCount) : ""
        }.Where(resultLine => !string.IsNullOrWhiteSpace(resultLine)).ToArray();

        return resultLines.Length == 0 ? GetLocalizedString("AccountsPage_ImportBackupEmptyResultMessage") : string.Join(Environment.NewLine, resultLines);
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
