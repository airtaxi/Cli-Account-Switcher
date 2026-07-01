using CliAccountSwitcher.Api.Providers.Ollama;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WinUIEx;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class OllamaWebViewWindow : ModalWindow
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private string _extractedAuthCookie;
    private string _extractedUserName;
    private string _extractedEmailAddress;
    private bool _isCompletingSuccessfully;

    public OllamaWebViewWindow(IntPtr parentWindowHandle) : base(parentWindowHandle)
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowTitle = _localizationService.GetLocalizedString("OllamaWebViewLoginAddAccountPage_TitleTextBlock.Text");
        Title = windowTitle;
        AppTitleBar.Title = windowTitle;

        _applicationThemeService.ApplyThemeToWindow(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;

        this.SetWindowSize(1000, 760);
        this.CenterOnScreen();

        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            WebViewLoadingPanel.Visibility = Visibility.Visible;
            LoginWebView2.Visibility = Visibility.Collapsed;

            await LoginWebView2.EnsureCoreWebView2Async();

            var coreWebView2 = LoginWebView2.CoreWebView2;
            coreWebView2.Settings.AreDevToolsEnabled = false;
            coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            coreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            coreWebView2.Settings.IsZoomControlEnabled = false;

            WebViewLoadingPanel.Visibility = Visibility.Collapsed;
            LoginWebView2.Visibility = Visibility.Visible;

            StartLoginFlow();
        }
        catch (Exception exception)
        {
            WebViewLoadingPanel.Visibility = Visibility.Collapsed;
            ShowError(exception.Message);
        }
    }

    private void StartLoginFlow()
    {
        var coreWebView2 = LoginWebView2.CoreWebView2;
        coreWebView2.CookieManager.DeleteAllCookies();

        var signInUri = new Uri(OllamaApiConventions.SettingsBaseUri, OllamaApiConventions.SignInPath).ToString();
        coreWebView2.Navigate(signInUri);

        StatusTextBlock.Text = "";
        ExtractionStatusPanel.Visibility = Visibility.Collapsed;
    }

    private void OnLoginWebView2NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess) return;
        _ = TryExtractCookieAndContinueAsync();
    }

    private async Task TryExtractCookieAndContinueAsync()
    {
        try
        {
            await ExtractAuthCookieAsync();
            if (string.IsNullOrWhiteSpace(_extractedAuthCookie)) return;

            StatusTextBlock.Text = _localizationService.GetLocalizedString("OllamaWebViewLoginAddAccountPage_ExtractingStatusText.Text");
            StatusProgressRing.IsActive = true;
            ExtractionStatusPanel.Visibility = Visibility.Visible;

            await ExtractEmailAddressAsync();

            var displayText = string.IsNullOrWhiteSpace(_extractedEmailAddress) ? (string.IsNullOrWhiteSpace(_extractedUserName) ? "Ready" : $"Ready: {_extractedUserName}") : $"Ready: {_extractedEmailAddress}";
            StatusTextBlock.Text = displayText;
            StatusProgressRing.IsActive = false;

            SendLoginResultAndClose();
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "";
            StatusProgressRing.IsActive = false;
            ShowError(exception.Message);
        }
    }

    private async Task ExtractAuthCookieAsync()
    {
        var coreWebView2 = LoginWebView2.CoreWebView2;
        var cookies = await coreWebView2.CookieManager.GetCookiesAsync(OllamaApiConventions.SettingsBaseUri.ToString());
        var authCookie = cookies.FirstOrDefault(cookie => string.Equals(cookie.Name, OllamaApiConventions.AuthCookieName, StringComparison.Ordinal));
        if (authCookie is null) return;
        _extractedAuthCookie = authCookie.Value;
    }

    private async Task ExtractEmailAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(_extractedAuthCookie)) return;

        var currentUri = LoginWebView2.Source?.ToString() ?? "";
        if (!currentUri.Contains(OllamaApiConventions.SettingsPath, StringComparison.OrdinalIgnoreCase))
        {
            var settingsUri = new Uri(OllamaApiConventions.SettingsBaseUri, OllamaApiConventions.SettingsPath).ToString();
            LoginWebView2.CoreWebView2.Navigate(settingsUri);
            await WaitForSettingsPageNavigationAsync();
        }

        var htmlContent = await LoginWebView2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
        var decodedHtml = DecodeJavaScriptString(htmlContent);
        (_extractedUserName, _extractedEmailAddress) = OllamaUsageClient.ParseUserIdentityFromHtml(decodedHtml);
    }

    private async Task WaitForSettingsPageNavigationAsync()
    {
        var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeoutCancellationTokenSource.Token.IsCancellationRequested)
        {
            var currentUri = LoginWebView2.Source?.ToString() ?? "";
            if (currentUri.Contains(OllamaApiConventions.SettingsPath, StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(100, timeoutCancellationTokenSource.Token);
        }
    }

    private void SendLoginResultAndClose()
    {
        _isCompletingSuccessfully = true;
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<OllamaLoginResultMessage>(new OllamaLoginResultMessage
        {
            IsSuccess = true,
            AuthCookie = _extractedAuthCookie,
            UserName = _extractedUserName,
            EmailAddress = _extractedEmailAddress
        }));
        Close();
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToWindow(this);

    private void OnOllamaWebViewWindowClosed(object sender, WindowEventArgs args)
    {
        _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;

        if (!_isCompletingSuccessfully) WeakReferenceMessenger.Default.Send(new ValueChangedMessage<OllamaLoginResultMessage>(new OllamaLoginResultMessage { IsSuccess = false }));
    }

    private static string DecodeJavaScriptString(string javaScriptString)
    {
        if (string.IsNullOrEmpty(javaScriptString)) return "";
        if (javaScriptString.Length >= 2 && javaScriptString[0] == '"' && javaScriptString[^1] == '"') javaScriptString = javaScriptString[1..^1];
        return javaScriptString.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }
}