using CodexAccountSwitch.Api.Authentication;
using CodexAccountSwitch.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace CodexAccountSwitch.WinUI.Pages.AddAccountDialog;

public sealed partial class OAuthAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private CancellationTokenSource _callbackCancellationTokenSource;
    private bool _isCompletingSuccessfully;

    public OAuthAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
    }

    private async void OnOAuthOpenBrowserButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;

        OAuthErrorInfoBar.IsOpen = false;
        OAuthOpenBrowserButton.IsEnabled = false;
        OAuthWaitingPanel.Visibility = Visibility.Visible;
        OAuthManualAddressPanel.Visibility = Visibility.Collapsed;
        _callbackCancellationTokenSource = new CancellationTokenSource();

        try
        {
            var codexOAuthSession = _addAccountDialogContext.CodexAccountService.CreateOAuthSession();
            await _addAccountDialogContext.SetOAuthSessionAsync(codexOAuthSession);
            OAuthManualAddressTextBox.Text = codexOAuthSession.AuthorizationAddress.ToString();
            if (!TryOpenBrowser(codexOAuthSession.AuthorizationAddress)) OAuthManualAddressPanel.Visibility = Visibility.Visible;

            var codexOAuthCallbackPayload = await codexOAuthSession.WaitForCallbackAsync(_callbackCancellationTokenSource.Token);
            MainWindow.Instance.BringToFront();

            _addAccountDialogContext.SetInteractionEnabled(false);
            await _addAccountDialogContext.CodexAccountService.AddOAuthCallbackAsync(codexOAuthSession, codexOAuthCallbackPayload, _callbackCancellationTokenSource.Token);
            await _addAccountDialogContext.DisposeOAuthSessionAsync();
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch (OperationCanceledException)
        {
            if (_callbackCancellationTokenSource?.IsCancellationRequested != true) ShowError();
        }
        catch { ShowError(); }
        finally
        {
            if (!_isCompletingSuccessfully && _addAccountDialogContext is not null) _addAccountDialogContext.SetInteractionEnabled(true);
            OAuthOpenBrowserButton.IsEnabled = !_isCompletingSuccessfully;
            OAuthWaitingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnOAuthManualAddressCopyButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(OAuthManualAddressTextBox.Text);
        Clipboard.SetContent(dataPackage);
    }

    private async void OnOAuthAddAccountPageUnloaded(object sender, RoutedEventArgs routedEventArguments)
    {
        _callbackCancellationTokenSource?.Cancel();
        if (_addAccountDialogContext is not null) await _addAccountDialogContext.DisposeOAuthSessionAsync();
        _callbackCancellationTokenSource?.Dispose();
    }

    private void ShowError()
    {
        OAuthErrorInfoBar.IsOpen = true;
        OAuthManualAddressPanel.Visibility = string.IsNullOrWhiteSpace(OAuthManualAddressTextBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool TryOpenBrowser(Uri authorizationAddress)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = authorizationAddress.ToString(),
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
    }
}
