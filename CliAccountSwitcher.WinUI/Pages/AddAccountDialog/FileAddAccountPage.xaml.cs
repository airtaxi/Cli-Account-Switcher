using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Dialogs;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using System.IO;

namespace CliAccountSwitcher.WinUI.Pages.AddAccountDialog;

public sealed partial class FileAddAccountPage : Page
{
    private static readonly Lazy<LocalizationService> s_localizationServiceLazy = new(() => App.Services.GetRequiredService<LocalizationService>());
    private static LocalizationService s_localizationService => s_localizationServiceLazy.Value;
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
            var authenticationDocumentText = await File.ReadAllTextAsync(storageFile.Path);
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
            var credentialsJson = await File.ReadAllTextAsync(credentialsStorageFile.Path);
            var globalConfigJson = await File.ReadAllTextAsync(globalConfigStorageFile.Path);
            await _addAccountDialogContext.SaveClaudeCodeAccountAsync(credentialsJson, globalConfigJson);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch
        {
            FileErrorInfoBar.Message = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeErrorMessage");
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
            FileTitleTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeTitle");
            FileDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeDescription");
            FileImportButtonTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeImportButtonText");
            FileSupportedFormatHeaderTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeSupportedFormatHeader");
            FileSupportedFormatDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeSupportedFormatDescription");
            FileErrorInfoBar.Message = s_localizationService.GetLocalizedString("FileAddAccountPage_ClaudeCodeErrorMessage");
            return;
        }

        FileTitleTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_TitleTextBlock/Text");
        FileDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_DescriptionTextBlock/Text");
        FileImportButtonTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_ImportButtonTextBlock/Text");
        FileSupportedFormatHeaderTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_SupportedFormatHeaderTextBlock/Text");
        FileSupportedFormatDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("FileAddAccountPage_SupportedFormatDescriptionTextBlock/Text");
    }

    private FileOpenPicker CreateAuthenticationFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileOpenPicker.FileTypeFilter.Add(".json");
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        return fileOpenPicker;
    }

}
