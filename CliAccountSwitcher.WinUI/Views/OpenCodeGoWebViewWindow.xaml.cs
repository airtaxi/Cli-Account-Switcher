using CliAccountSwitcher.Api.Providers.OpenCodeGo;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models;
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
using System.Text.RegularExpressions;
using WinUIEx;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class OpenCodeGoWebViewWindow : ModalWindow
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly OpenCodeGoAccountService _openCodeGoAccountService = App.Services.GetRequiredService<OpenCodeGoAccountService>();
    private string _extractedAuthCookie;
    private string _extractedWorkspaceId;
    private OpenCodeGoKeyInfo _extractedKeyInfo;
    private bool _isAwaitingKeysPageNavigation;
    private bool _isCompletingSuccessfully;

    private static readonly Regex s_workspaceIdPattern = new(@"/workspace/(wrk_[^/""&?]+)", RegexOptions.Compiled);

    public OpenCodeGoWebViewWindow(IntPtr parentWindowHandle) : base(parentWindowHandle)
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowTitle = _localizationService.GetLocalizedString("OpenCodeGoWebViewLoginAddAccountPage_TitleTextBlock.Text");
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

        var authorizeUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, OpenCodeGoApiConventions.AuthAuthorizePath).ToString();
        coreWebView2.Navigate(authorizeUri);

        StatusTextBlock.Text = "";
        ExtractionStatusPanel.Visibility = Visibility.Collapsed;
    }

    private void OnLoginWebView2NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess) return;

        var currentUri = sender.Source;
        var currentUriString = currentUri?.ToString() ?? "";

        if (_isAwaitingKeysPageNavigation)
        {
            _isAwaitingKeysPageNavigation = false;
            return;
        }

        if (currentUriString.Contains("/workspace/wrk_", StringComparison.Ordinal)) TryExtractWorkspaceIdAndContinue(currentUriString);
    }

    private async void TryExtractWorkspaceIdAndContinue(string currentUriString)
    {
        var match = s_workspaceIdPattern.Match(currentUriString);
        if (!match.Success) return;

        _extractedWorkspaceId = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(_extractedWorkspaceId)) return;

        StatusTextBlock.Text = "Login detected. Extracting API key...";
        StatusProgressRing.IsActive = true;
        ExtractionStatusPanel.Visibility = Visibility.Visible;

        try
        {
            await ExtractAuthCookieAsync();
            await ExtractApiKeyAsync();
            await ExtractAccountEmailAsync();

            StatusTextBlock.Text = $"Ready: {_extractedKeyInfo?.Name ?? "API Key"} ({_extractedKeyInfo?.Email ?? ""})";
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
        var cookies = await coreWebView2.CookieManager.GetCookiesAsync(OpenCodeGoApiConventions.ConsoleBaseUri.ToString());
        var authCookie = cookies.FirstOrDefault(cookie => string.Equals(cookie.Name, OpenCodeGoApiConventions.AuthCookieName, StringComparison.Ordinal));
        if (authCookie is null) throw new InvalidOperationException("Auth cookie not found after login.");
        _extractedAuthCookie = authCookie.Value;
    }

    private async Task ExtractApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(_extractedWorkspaceId) || string.IsNullOrWhiteSpace(_extractedAuthCookie)) return;

        var keysPageUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, string.Format(OpenCodeGoApiConventions.KeysPagePathTemplate, _extractedWorkspaceId)).ToString();

        _isAwaitingKeysPageNavigation = true;
        LoginWebView2.CoreWebView2.Navigate(keysPageUri);

        await WaitForKeysPageNavigationAsync();

        var htmlContent = await LoginWebView2.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
        var decodedHtml = DecodeJavaScriptString(htmlContent);
        _extractedKeyInfo = ParseKeyInfoFromHtml(decodedHtml);
        if (_extractedKeyInfo is null) throw new InvalidOperationException("No API key found on the keys page.");
    }

    private async Task WaitForKeysPageNavigationAsync()
    {
        var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (_isAwaitingKeysPageNavigation && !timeoutCancellationTokenSource.Token.IsCancellationRequested) await Task.Delay(100, timeoutCancellationTokenSource.Token);

        if (_isAwaitingKeysPageNavigation) throw new TimeoutException("Timed out waiting for the API keys page to load.");
    }

    private async Task ExtractAccountEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(_extractedAuthCookie)) return;

        if (string.IsNullOrWhiteSpace(_extractedKeyInfo?.Email))
        {
            var email = await _openCodeGoAccountService.GetAccountEmailAsync(_extractedAuthCookie);
            if (!string.IsNullOrWhiteSpace(email) && _extractedKeyInfo is not null) _extractedKeyInfo.Email = email;
        }
    }

    private void SendLoginResultAndClose()
    {
        _isCompletingSuccessfully = true;
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<OpenCodeGoLoginResultMessage>(new OpenCodeGoLoginResultMessage
        {
            IsSuccess = true,
            AuthCookie = _extractedAuthCookie,
            WorkspaceId = _extractedWorkspaceId,
            KeyInfo = _extractedKeyInfo
        }));
        Close();
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToWindow(this);

    private void OnOpenCodeGoWebViewWindowClosed(object sender, WindowEventArgs args)
    {
        _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;

        if (!_isCompletingSuccessfully) WeakReferenceMessenger.Default.Send(new ValueChangedMessage<OpenCodeGoLoginResultMessage>(new OpenCodeGoLoginResultMessage { IsSuccess = false }));
    }

    private static OpenCodeGoKeyInfo ParseKeyInfoFromHtml(string htmlText)
    {
        var keyPattern = new Regex(@"\{id:""key_[^""]*"",name:""([^""]*)"",key:""(sk-[^""]+)"",timeUsed:null,userID:""[^""]*"",(?:email:""([^""]*)"",)?keyDisplay:""([^""]*)""\}", RegexOptions.Compiled);
        var match = keyPattern.Match(htmlText);
        if (!match.Success) return null;

        return new OpenCodeGoKeyInfo
        {
            Name = match.Groups[1].Value,
            Key = match.Groups[2].Value,
            Email = match.Groups[3].Success ? match.Groups[3].Value : "",
            KeyDisplay = match.Groups[4].Value
        };
    }

    private static string DecodeJavaScriptString(string javaScriptString)
    {
        if (string.IsNullOrEmpty(javaScriptString)) return "";
        if (javaScriptString.Length >= 2 && javaScriptString[0] == '"' && javaScriptString[^1] == '"') javaScriptString = javaScriptString[1..^1];
        return javaScriptString.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }
}
