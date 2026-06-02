using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Infrastructure;
using CliAccountSwitcher.WinUI.Dialogs;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CliAccountSwitcher.WinUI.Pages.AddAccountDialog;

public sealed partial class CurrentAccountAddAccountPage : Page
{
    private static readonly Lazy<LocalizationService> s_localizationServiceLazy = new(() => App.Services.GetRequiredService<LocalizationService>());
    private static LocalizationService s_localizationService => s_localizationServiceLazy.Value;
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
            CurrentAccountTitleTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeTitle");
            CurrentAccountDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeDescription");
            CurrentAccountImportButtonTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeImportButtonText");
            CurrentAccountFilePathHeaderTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeFilePathHeader");
            CurrentAccountErrorInfoBar.Message = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ClaudeCodeErrorMessage");
            return;
        }

        CurrentAccountTitleTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_TitleTextBlock/Text");
        CurrentAccountDescriptionTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_DescriptionTextBlock/Text");
        CurrentAccountImportButtonTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_ImportButtonTextBlock/Text");
        CurrentAccountFilePathHeaderTextBlock.Text = s_localizationService.GetLocalizedString("CurrentAccountAddAccountPage_FilePathHeaderTextBlock/Text");
    }

}
