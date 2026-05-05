using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Infrastructure;
using CliAccountSwitcher.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CliAccountSwitcher.WinUI.Pages.AddAccountDialog;

public sealed partial class CurrentAccountAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

#pragma warning disable CA1822 // Mark members as static => Used in XAML binding, so it cannot be static
    public string CurrentAuthenticationFilePath
    {
        get
        {
            if (_addAccountDialogContext?.SelectedProviderKind != CliProviderKind.ClaudeCode) return Constants.CurrentAuthenticationFilePath;

            var claudeCodePaths = new ClaudeCodePaths();
            return $"{claudeCodePaths.CredentialsFilePath}{Environment.NewLine}{claudeCodePaths.GlobalConfigFilePath}";
        }
    }
#pragma warning restore CA1822 // Mark members as static => Used in XAML binding, so it cannot be static

    public CurrentAccountAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
        RefreshProviderSpecificText();
        Bindings.Update();
    }

    private async void OnImportCurrentAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;

        CurrentAccountErrorInfoBar.IsOpen = false;
        CurrentAccountValidationProgressRing.IsActive = true;
        CurrentAccountValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            if (_addAccountDialogContext.SelectedProviderKind == CliProviderKind.Codex) await _addAccountDialogContext.CodexAccountService.AddCurrentAuthenticationDocumentAsync();
            else await _addAccountDialogContext.SaveCurrentClaudeCodeAccountAsync();
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch { CurrentAccountErrorInfoBar.IsOpen = true; }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            CurrentAccountValidationProgressRing.IsActive = false;
            CurrentAccountValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshProviderSpecificText()
    {
        if (_addAccountDialogContext?.SelectedProviderKind == CliProviderKind.ClaudeCode)
        {
            CurrentAccountTitleTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeTitle");
            CurrentAccountDescriptionTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeDescription");
            CurrentAccountImportButtonTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeImportButtonText");
            CurrentAccountFilePathHeaderTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeFilePathHeader");
            CurrentAccountErrorInfoBar.Message = GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeErrorMessage");
            return;
        }

        CurrentAccountTitleTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_TitleTextBlock/Text");
        CurrentAccountDescriptionTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_DescriptionTextBlock/Text");
        CurrentAccountImportButtonTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_ImportButtonTextBlock/Text");
        CurrentAccountFilePathHeaderTextBlock.Text = GetLocalizedString("CurrentAccountAddAccountPage_FilePathHeaderTextBlock/Text");
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);
}
