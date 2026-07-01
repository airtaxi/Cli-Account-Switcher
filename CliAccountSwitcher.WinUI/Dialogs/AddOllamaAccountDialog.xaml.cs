using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.Views;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed partial class AddOllamaAccountDialog : ContentDialog
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly OllamaAccountService _ollamaAccountService = App.Services.GetRequiredService<OllamaAccountService>();
    private OllamaLoginResultMessage _loginResult;
    private bool _isAddingAccount;

    public AddOllamaAccountDialog()
    {
        InitializeComponent();
        _applicationThemeService.ApplyThemeToElement(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<OllamaLoginResultMessage>>(this, OnLoginResultReceived);
    }

    private void OnSignInButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        ErrorInfoBar.IsOpen = false;
        SignInButton.IsEnabled = false;
        var webViewWindow = new OllamaWebViewWindow(WindowNative.GetWindowHandle(MainWindow.Instance));
        webViewWindow.Activate();
    }

    private void OnLoginResultReceived(object recipient, ValueChangedMessage<OllamaLoginResultMessage> valueChangedMessage)
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => ProcessLoginResult(valueChangedMessage.Value)); return; }
        ProcessLoginResult(valueChangedMessage.Value);
    }

    private void ProcessLoginResult(OllamaLoginResultMessage loginResult)
    {
        SignInButton.IsEnabled = true;
        _loginResult = loginResult;

        if (loginResult.IsSuccess)
        {
            var displayText = string.IsNullOrWhiteSpace(loginResult.EmailAddress) ? (string.IsNullOrWhiteSpace(loginResult.UserName) ? "Ready" : $"Ready: {loginResult.UserName}") : $"Ready: {loginResult.EmailAddress}";
            StatusTextBlock.Text = displayText;
            StatusPanel.Visibility = Visibility.Visible;
            AddAccountButton.IsEnabled = true;
            return;
        }

        StatusTextBlock.Text = "";
        StatusPanel.Visibility = Visibility.Collapsed;
        AddAccountButton.IsEnabled = false;
    }

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_loginResult is null || !_loginResult.IsSuccess || string.IsNullOrWhiteSpace(_loginResult.AuthCookie)) return;

        ErrorInfoBar.IsOpen = false;
        StatusProgressRing.IsActive = true;
        StatusProgressRing.Visibility = Visibility.Visible;
        AddAccountButton.IsEnabled = false;
        IsEnabled = false;

        try
        {
            await _ollamaAccountService.AddAccountFromWebViewAsync(_loginResult.AuthCookie, _loginResult.UserName, _loginResult.EmailAddress, AliasTextBox.Text);
            _isAddingAccount = true;
            Hide();
        }
        catch (Exception exception) { ShowError(exception.Message); }
        finally
        {
            if (!_isAddingAccount) IsEnabled = true;
            StatusProgressRing.IsActive = false;
            StatusProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToElement(this);

    private void OnAddOllamaAccountDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs contentDialogClosingEventArguments)
    {
        _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<OllamaLoginResultMessage>>(this);
    }
}