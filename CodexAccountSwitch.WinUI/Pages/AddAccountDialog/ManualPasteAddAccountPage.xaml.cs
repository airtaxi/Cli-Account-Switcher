using CodexAccountSwitch.WinUI.Dialogs;
using CodexAccountSwitch.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodexAccountSwitch.WinUI.Pages.AddAccountDialog;

public sealed partial class ManualPasteAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public ObservableCollection<ManualAuthenticationInputViewModel> ManualAuthenticationInputs { get; } = [new()];

    public ManualPasteAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
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

            ManualErrorInfoBar.Message = successCount == 0 && failureCount == 0 ? "붙여넣은 auth.json이 없습니다." : "일부 auth.json을 불러오는 데 실패했습니다.";
            ManualErrorInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            ManualValidationProgressRing.IsActive = false;
            ManualValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }
}
