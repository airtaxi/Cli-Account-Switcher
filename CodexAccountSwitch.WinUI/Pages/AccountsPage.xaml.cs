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
        ViewModel = new AccountsPageViewModel(App.CodexAccountService, DispatcherQueue);
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync("전체 계정 사용량을 새로고침하는 중입니다.", async () => await App.CodexAccountService.RefreshAllAccountsAsync());

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
        await RunWithLoadingAsync("선택한 계정 사용량을 새로고침하는 중입니다.", async () => await App.CodexAccountService.RefreshAccountsAsync(selectedAccountIdentifiers));
    }

    private async void OnDeleteSelectedAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedAccountIdentifiers = ViewModel.SelectedAccountIdentifiers;
        if (selectedAccountIdentifiers.Count == 0) return;

        var contentDialogResult = await this.ShowDialogAsync("선택 계정 삭제", $"{selectedAccountIdentifiers.Count}개 계정을 삭제할까요?", "삭제", "취소");
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
        await this.ShowDialogAsync("백업 내보내기", "계정 백업을 저장했습니다.");
    }

    private async void OnImportBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateZipFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading("백업 파일의 계정을 검증하는 중입니다.");
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

        await this.ShowDialogAsync("백업 불러오기", BuildBackupImportResultText(codexAccountBackupImportResult.SuccessCount, codexAccountBackupImportResult.FailureCount, codexAccountBackupImportResult.DuplicateCount));
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync("만료 계정 정리", "토큰이 만료된 계정을 계정 목록에서 삭제할까요?", "삭제", "취소");
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedCount = await App.CodexAccountService.DeleteExpiredAccountsAsync();
        ViewModel.ReloadAccounts();
        await this.ShowDialogAsync("만료 계정 정리", deletedCount == 0 ? "삭제할 만료 계정이 없습니다." : $"{deletedCount}개 계정을 삭제했습니다.");
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

        var customAlias = await this.ShowInputDialogAsync("계정 이름 변경", "새 이름", true);
        if (string.IsNullOrWhiteSpace(customAlias)) return;

        await App.CodexAccountService.RenameAccountAsync(accountIdentifier, customAlias);
        ViewModel.ReloadAccounts();
    }

    private async void OnRefreshAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        await RunWithLoadingAsync("계정 사용량을 새로고침하는 중입니다.", async () => await App.CodexAccountService.RefreshAccountsAsync([accountIdentifier]));
    }

    private async void OnDeleteAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var accountIdentifier = ReadCommandParameter(sender);
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var contentDialogResult = await this.ShowDialogAsync("계정 삭제", "이 계정을 삭제할까요?", "삭제", "취소");
        if (contentDialogResult != ContentDialogResult.Primary) return;

        await App.CodexAccountService.DeleteAccountsAsync([accountIdentifier]);
        ViewModel.ReloadAccounts();
    }

    private void OnAccountSearchAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs autoSuggestBoxTextChangedEventArguments) => ViewModel.SearchText = sender.Text;

    private void OnPlanFilterSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => ViewModel.SelectedPlanFilter = sender.SelectedItem?.Tag as string ?? "All";

    private void OnAccountsListViewSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        ViewModel.SetSelectedAccountIdentifiers(AccountsListView.SelectedItems.OfType<CodexAccountViewModel>().Select(accountViewModel => accountViewModel.AccountIdentifier));
    }

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
        fileSavePicker.FileTypeChoices.Add("ZIP 백업", [".zip"]);
        return fileSavePicker;
    }

    private static string ReadCommandParameter(object sender) => sender is FrameworkElement { Tag: string accountIdentifier } ? accountIdentifier : sender is Button { CommandParameter: string buttonAccountIdentifier } ? buttonAccountIdentifier : "";

    private static string BuildBackupImportResultText(int successCount, int failureCount, int duplicateCount)
    {
        var resultLines = new[]
        {
            successCount > 0 ? $"{successCount}개 성공" : "",
            failureCount > 0 ? $"{failureCount}개 실패" : "",
            duplicateCount > 0 ? $"{duplicateCount}개 중복" : ""
        }.Where(resultLine => !string.IsNullOrWhiteSpace(resultLine)).ToArray();

        return resultLines.Length == 0 ? "불러올 계정이 없습니다." : string.Join(Environment.NewLine, resultLines);
    }
}
