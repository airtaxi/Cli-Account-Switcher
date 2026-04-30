using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Dialogs;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Pages.AddAccountDialog;

public sealed partial class FileAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public FileAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
        RefreshProviderSpecificText();
    }

    private async void OnImportAuthenticationFileButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;
        if (_addAccountDialogContext.SelectedProviderKind == CliProviderKind.ClaudeCode)
        {
            await ImportClaudeCodeFilesAsync();
            return;
        }

        var fileOpenPicker = CreateAuthenticationFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        FileErrorInfoBar.IsOpen = false;
        FileValidationProgressRing.IsActive = true;
        FileValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            var authenticationDocumentText = await FileIO.ReadTextAsync(storageFile);
            await _addAccountDialogContext.CodexAccountService.AddValidatedAuthenticationDocumentTextAsync(authenticationDocumentText);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch { FileErrorInfoBar.IsOpen = true; }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            FileValidationProgressRing.IsActive = false;
            FileValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ImportClaudeCodeFilesAsync()
    {
        var credentialsFileOpenPicker = CreateAuthenticationFileOpenPicker();
        var credentialsStorageFile = await credentialsFileOpenPicker.PickSingleFileAsync();
        if (credentialsStorageFile is null) return;

        var globalConfigFileOpenPicker = CreateAuthenticationFileOpenPicker();
        var globalConfigStorageFile = await globalConfigFileOpenPicker.PickSingleFileAsync();
        if (globalConfigStorageFile is null) return;

        FileErrorInfoBar.IsOpen = false;
        FileValidationProgressRing.IsActive = true;
        FileValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            var credentialsJson = await FileIO.ReadTextAsync(credentialsStorageFile);
            var globalConfigJson = await FileIO.ReadTextAsync(globalConfigStorageFile);
            await _addAccountDialogContext.SaveClaudeCodeAccountAsync(credentialsJson, globalConfigJson);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch
        {
            FileErrorInfoBar.Message = GetLocalizedString("FileAddAccountPage_ClaudeCodeErrorMessage");
            FileErrorInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            FileValidationProgressRing.IsActive = false;
            FileValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshProviderSpecificText()
    {
        if (_addAccountDialogContext?.SelectedProviderKind == CliProviderKind.ClaudeCode)
        {
            FileTitleTextBlock.Text = GetLocalizedString("FileAddAccountPage_ClaudeCodeTitle");
            FileDescriptionTextBlock.Text = GetLocalizedString("FileAddAccountPage_ClaudeCodeDescription");
            FileImportButtonTextBlock.Text = GetLocalizedString("FileAddAccountPage_ClaudeCodeImportButtonText");
            FileSupportedFormatHeaderTextBlock.Text = GetLocalizedString("FileAddAccountPage_ClaudeCodeSupportedFormatHeader");
            FileSupportedFormatDescriptionTextBlock.Text = GetLocalizedString("FileAddAccountPage_ClaudeCodeSupportedFormatDescription");
            FileErrorInfoBar.Message = GetLocalizedString("FileAddAccountPage_ClaudeCodeErrorMessage");
            return;
        }

        FileTitleTextBlock.Text = GetLocalizedString("FileAddAccountPage_TitleTextBlock/Text");
        FileDescriptionTextBlock.Text = GetLocalizedString("FileAddAccountPage_DescriptionTextBlock/Text");
        FileImportButtonTextBlock.Text = GetLocalizedString("FileAddAccountPage_ImportButtonTextBlock/Text");
        FileSupportedFormatHeaderTextBlock.Text = GetLocalizedString("FileAddAccountPage_SupportedFormatHeaderTextBlock/Text");
        FileSupportedFormatDescriptionTextBlock.Text = GetLocalizedString("FileAddAccountPage_SupportedFormatDescriptionTextBlock/Text");
    }

    private static FileOpenPicker CreateAuthenticationFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileOpenPicker.FileTypeFilter.Add(".json");
        return fileOpenPicker;
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);
}
