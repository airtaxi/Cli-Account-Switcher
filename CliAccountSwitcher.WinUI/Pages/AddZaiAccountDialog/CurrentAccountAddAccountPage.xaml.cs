using CliAccountSwitcher.Api.Providers.Zai.Infrastructure;
using CliAccountSwitcher.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CliAccountSwitcher.WinUI.Pages.AddZaiAccountDialog;

public sealed partial class CurrentAccountAddAccountPage : Page
{
    private AddZaiAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

#pragma warning disable CA1822 // Mark members as static => Used in XAML binding, so it cannot be static
    public string ChelperConfigFilePath => new ZaiChelperConfigPaths().ConfigFilePath;
#pragma warning restore CA1822 // Mark members as static => Used in XAML binding, so it cannot be static

    public CurrentAccountAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments) => _addAccountDialogContext = navigationEventArguments.Parameter as AddZaiAccountDialogContext;

    private async void OnImportCurrentAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;

        ErrorInfoBar.IsOpen = false;
        ValidationProgressRing.IsActive = true;
        ValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            await _addAccountDialogContext.ZaiAccountService.AddChelperConfigAccountAsync();
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
