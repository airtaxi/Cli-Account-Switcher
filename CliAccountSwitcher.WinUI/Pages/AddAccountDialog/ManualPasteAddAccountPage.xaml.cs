using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Dialogs;
using CliAccountSwitcher.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;

namespace CliAccountSwitcher.WinUI.Pages.AddAccountDialog;

public sealed partial class ManualPasteAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public ObservableCollection<ManualAuthenticationInputViewModel> ManualAuthenticationInputs { get; } = [new()];

    public ManualPasteAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
        RefreshProviderSpecificLayout();
    }

    private void OnAddManualAuthenticationTextBoxButtonClicked(object sender, RoutedEventArgs routedEventArguments) => ManualAuthenticationInputs.Add(new ManualAuthenticationInputViewModel());

    private void OnRemoveManualAuthenticationTextBoxButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (sender is not Button { CommandParameter: ManualAuthenticationInputViewModel manualAuthenticationInputViewModel }) return;
        if (ManualAuthenticationInputs.Count == 1)
        {
            manualAuthenticationInputViewModel.AuthenticationDocumentText = "";
            manualAuthenticationInputViewModel.HasError = false;
            return;
        }

        ManualAuthenticationInputs.Remove(manualAuthenticationInputViewModel);
    }

    private async void OnLoadManualAuthenticationDocumentsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;
        if (_addAccountDialogContext.SelectedProviderKind == CliProviderKind.ClaudeCode)
        {
            await SaveClaudeCodeManualAccountAsync();
            return;
        }

        ManualErrorInfoBar.IsOpen = false;
        ManualValidationProgressRing.IsActive = true;
        ManualValidationProgressRing.Visibility = Visibility.Visible;
        foreach (var manualAuthenticationInput in ManualAuthenticationInputs) manualAuthenticationInput.HasError = false;

        _addAccountDialogContext.SetInteractionEnabled(false);
        var successCount = 0;
        var failureCount = 0;

        try
        {
            foreach (var manualAuthenticationInput in ManualAuthenticationInputs.Where(manualAuthenticationInput => !string.IsNullOrWhiteSpace(manualAuthenticationInput.AuthenticationDocumentText)))
            {
                try
                {
                    await _addAccountDialogContext.CodexAccountService.AddValidatedAuthenticationDocumentTextAsync(manualAuthenticationInput.AuthenticationDocumentText);
                    successCount++;
                }
                catch
                {
                    manualAuthenticationInput.HasError = true;
                    failureCount++;
                }
            }

            if (successCount > 0 && failureCount == 0)
            {
                _isCompletingSuccessfully = true;
                _addAccountDialogContext.CompleteSuccessfully();
                return;
            }

            ManualErrorInfoBar.Message = successCount == 0 && failureCount == 0 ? GetLocalizedString("ManualPasteAddAccountPage_NoInputMessage") : GetLocalizedString("ManualPasteAddAccountPage_PartialFailureMessage");
            ManualErrorInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            ManualValidationProgressRing.IsActive = false;
            ManualValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SaveClaudeCodeManualAccountAsync()
    {
        ManualErrorInfoBar.IsOpen = false;
        ManualValidationProgressRing.IsActive = true;
        ManualValidationProgressRing.Visibility = Visibility.Visible;

        _addAccountDialogContext.SetInteractionEnabled(false);
        try
        {
            await _addAccountDialogContext.SaveClaudeCodeAccountAsync(ClaudeCodeCredentialsJsonTextBox.Text, ClaudeCodeGlobalConfigJsonTextBox.Text);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch
        {
            ManualErrorInfoBar.Message = GetLocalizedString("ManualPasteAddAccountPage_ClaudeCodeValidationErrorMessage");
            ManualErrorInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            ManualValidationProgressRing.IsActive = false;
            ManualValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshProviderSpecificLayout()
    {
        var isClaudeCodeProviderSelected = _addAccountDialogContext?.SelectedProviderKind == CliProviderKind.ClaudeCode;
        AddManualAuthenticationTextBoxButton.Visibility = isClaudeCodeProviderSelected ? Visibility.Collapsed : Visibility.Visible;
        ManualAuthenticationInputsListView.Visibility = isClaudeCodeProviderSelected ? Visibility.Collapsed : Visibility.Visible;
        ClaudeCodeManualInputGrid.Visibility = isClaudeCodeProviderSelected ? Visibility.Visible : Visibility.Collapsed;

        if (isClaudeCodeProviderSelected)
        {
            ManualTitleTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_ClaudeCodeTitle");
            ManualDescriptionTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_ClaudeCodeDescription");
            ManualLoadInputsTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_ClaudeCodeLoadInputsButtonText");
            ManualErrorInfoBar.Message = GetLocalizedString("ManualPasteAddAccountPage_ClaudeCodeValidationErrorMessage");
            return;
        }

        ManualTitleTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_TitleTextBlock/Text");
        ManualDescriptionTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_DescriptionTextBlock/Text");
        ManualLoadInputsTextBlock.Text = GetLocalizedString("ManualPasteAddAccountPage_LoadInputsTextBlock/Text");
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);
}
