using CliAccountSwitcher.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CliAccountSwitcher.WinUI.Pages.AddZaiAccountDialog;

public sealed partial class ApiKeyAddAccountPage : Page
{
    private AddZaiAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public ApiKeyAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments) => _addAccountDialogContext = navigationEventArguments.Parameter as AddZaiAccountDialogContext;

    private async void OnAddApiKeyButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;

        var apiKey = ApiKeyPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ErrorInfoBar.IsOpen = true;
            return;
        }

        ErrorInfoBar.IsOpen = false;
        ValidationProgressRing.IsActive = true;
        ValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            await _addAccountDialogContext.ZaiAccountService.AddApiKeyAsync(apiKey, AliasTextBox.Text, false);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch { ErrorInfoBar.IsOpen = true; }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            ValidationProgressRing.IsActive = false;
            ValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }
}
