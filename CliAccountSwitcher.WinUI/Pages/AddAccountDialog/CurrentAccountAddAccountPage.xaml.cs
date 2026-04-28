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
    public string CurrentAuthenticationFilePath => Constants.CurrentAuthenticationFilePath;
#pragma warning restore CA1822 // Mark members as static => Used in XAML binding, so it cannot be static

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
