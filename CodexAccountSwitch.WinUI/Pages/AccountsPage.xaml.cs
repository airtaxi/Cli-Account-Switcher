using CodexAccountSwitch.WinUI.Helpers;
using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class AccountsPage : Page
{
    public AccountsPageViewModel ViewModel { get; }

    public AccountsPage()
    {
        ViewModel = new AccountsPageViewModel(App.CodexAccountService, App.ApplicationSettings, DispatcherQueue);
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await App.CodexAccountService.RefreshAllAccountsAsync());

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CodexAccountSwitch.WinUI.Dialogs.AddAccountDialog(App.CodexAccountService)
        {
            XamlRoot = XamlRoot
        };
        await addAccountDialog.ShowAsync();
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;
        await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshSelectedAccountsLoadingMessage"), async () => await App.CodexAccountService.RefreshAccountsAsync(selectedAccountIdentifiers));
    }

    private async void OnDeleteSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;

        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteSelectedAccountsDialogTitle"), GetFormattedString("AccountsPage_DeleteSelectedAccountsDialogMessage", selectedAccountIdentifiers.Count), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await App.CodexAccountService.DeleteAccountsAsync(selectedAccountIdentifiers);
        ViewModel.ReloadAccounts();
    }

    private async void OnExportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileSavePicker = CreateZipFileSavePicker();
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        await App.CodexAccountService.ExportBackupAsync(storageFile.Path);
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_ExportBackupDialogTitle"), GetLocalizedString("AccountsPage_ExportBackupDialogMessage"));
    }

    private async void OnImportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateZipFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(GetLocalizedString("AccountsPage_ImportBackupLoadingMessage"));
        var codexAccountBackupImportResult = default(CodexAccountBackupImportResult);
        try
        {
            codexAccountBackupImportResult = await App.CodexAccountService.ImportBackupAsync(storageFile.Path);
            ViewModel.ReloadAccounts();
        }
        catch { codexAccountBackupImportResult = new CodexAccountBackupImportResult { FailureCount = 1 }; }
        finally
        {
            MainWindow.HideLoading();
        }

        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_ImportBackupDialogTitle"), BuildBackupImportResultText(codexAccountBackupImportResult.SuccessCount, codexAccountBackupImportResult.FailureCount, codexAccountBackupImportResult.DuplicateCount));
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedCount = await App.CodexAccountService.DeleteExpiredAccountsAsync();
        ViewModel.ReloadAccounts();
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedCount == 0 ? GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedCount));
    }

    private async void OnSwitchAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await App.CodexAccountService.SwitchActiveAccountAsync(accountIdentifier);
        ViewModel.ReloadAccounts();
    }

    private async void OnRenameAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var currentAccountDisplayName = ViewModel.Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))?.DisplayName ?? "";
        var customAlias = await this.ShowInputDialogAsync(GetLocalizedString("AccountsPage_RenameAccountDialogTitle"), GetLocalizedString("AccountsPage_RenameAccountPlaceholderText"), true, defaultText: currentAccountDisplayName);
        if (string.IsNullOrWhiteSpace(customAlias)) return;

        await App.CodexAccountService.RenameAccountAsync(accountIdentifier, customAlias);
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage"), async () => await App.CodexAccountService.RefreshAccountsAsync([accountIdentifier]));
    }

    private async void OnDeleteAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteAccountDialogTitle"), GetLocalizedString("AccountsPage_DeleteAccountDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await App.CodexAccountService.DeleteAccountsAsync([accountIdentifier]);
        ViewModel.ReloadAccounts();
    }

    private void OnAccountSearchAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs autoSuggestBoxTextChangedEventArguments) => ViewModel.SearchText = sender.Text;

    private void OnPlanFilterSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => ViewModel.SelectedPlanFilter = sender.SelectedItem?.Tag as string ?? "All";

    private void OnAccountsListViewSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments) => ViewModel.SetSelectedAccountIdentifiers(AccountsListView.SelectedItems.OfType<CodexAccountViewModel>().Select(accountViewModel => accountViewModel.AccountIdentifier));

    private void OnAccountsPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action)
    {
        MainWindow.ShowLoading(loadingMessage);
        try
        {
            await action();
            ViewModel.ReloadAccounts();
        }
        finally
        {
            MainWindow.HideLoading();
        }
    }

    private static FileOpenPicker CreateZipFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileOpenPicker.FileTypeFilter.Add(".zip");
        return fileOpenPicker;
    }

    private static FileSavePicker CreateZipFileSavePicker()
    {
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"codex-accounts-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}"
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(GetLocalizedString("AccountsPage_ZipBackupFileTypeChoice"), [".zip"]);
        return fileSavePicker;
    }

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
