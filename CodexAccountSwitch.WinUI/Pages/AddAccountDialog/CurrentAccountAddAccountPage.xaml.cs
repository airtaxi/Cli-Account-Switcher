using CodexAccountSwitch.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CodexAccountSwitch.WinUI.Pages.AddAccountDialog;

public sealed partial class CurrentAccountAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public string CurrentAuthenticationFilePath => Constants.CurrentAuthenticationFilePath;

    public CurrentAccountAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
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
            await _addAccountDialogContext.CodexAccountService.AddCurrentAuthenticationDocumentAsync();
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
}
