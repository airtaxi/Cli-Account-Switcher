using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using System;
using System.Threading.Tasks;
using WinUIEx;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class PopupWindow : WindowEx, IDisposable
{
    private bool _isApplyingProviderSelection;
    private bool _disposed;

    public PopupWindow()
    {
        ViewModel = new DashboardPageViewModel(App.CodexAccountService, App.ApplicationSettings, DispatcherQueue);

        InitializeComponent();

        App.ApplicationThemeService.ApplyThemeToWindow(this);
        WindowInteractionHelper.DisableWindowAnimations(this);
        if (Content is FrameworkElement { RequestedTheme: ElementTheme.Dark }) WindowInteractionHelper.SetDarkModeWindow(this);

        this.SetIsAlwaysOnTop(true);
        AppWindow.IsShownInSwitchers = false;

        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter) overlappedPresenter.SetBorderAndTitleBar(true, false);

        App.ApplicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
        App.LocalizationService.LanguageChanged += RefreshLocalizedText;
        WeakReferenceMessenger.Default.Register<ActiveAccountQuotaRefreshRequestedMessage>(this, OnActiveAccountQuotaRefreshRequestedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ApplyProviderSelection(App.ApplicationSettings.SelectedProviderKind);
        RefreshLocalizedText();
    }

    public DashboardPageViewModel ViewModel { get; }

    public void ShowLoading(string message = null)
    {
        if (DispatcherQueue.HasThreadAccess) ShowLoadingCore(message);
        else DispatcherQueue.TryEnqueue(() => ShowLoadingCore(message));
    }

    public void HideLoading()
    {
        if (DispatcherQueue.HasThreadAccess) HideLoadingCore();
        else DispatcherQueue.TryEnqueue(HideLoadingCore);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Activated -= OnPopupWindowActivated;
        Closed -= OnPopupWindowClosed;
        App.ApplicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        App.LocalizationService.LanguageChanged -= RefreshLocalizedText;
        WeakReferenceMessenger.Default.Unregister<ActiveAccountQuotaRefreshRequestedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ShowLoadingCore(string message)
    {
        RootFrame.IsEnabled = false;

        if (string.IsNullOrWhiteSpace(message)) LoadingTextBlock.Visibility = Visibility.Collapsed;
        else
        {
            LoadingTextBlock.Visibility = Visibility.Visible;
            LoadingTextBlock.Text = message;
        }

        LoadingGrid.Visibility = Visibility.Visible;
    }

    private void HideLoadingCore()
    {
        LoadingGrid.Visibility = Visibility.Collapsed;
        RootFrame.IsEnabled = true;
    }

    private void OnPopupWindowActivated(object sender, WindowActivatedEventArgs windowActivatedEventArguments)
    {
        if (windowActivatedEventArguments.WindowActivationState == WindowActivationState.Deactivated) Close();
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs routedEventArguments) => WindowInteractionHelper.ForceForegroundWindow(this);

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToWindow(this);

    private async void OnActiveAccountQuotaRefreshRequestedMessageReceived(object messageRecipient, ActiveAccountQuotaRefreshRequestedMessage activeAccountQuotaRefreshRequestedMessage) => await RunWithLoadingAsync(App.LocalizationService.GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage"), async () => await ViewModel.RefreshActiveProviderAccountAsync());

    private void OnProviderKindChangedMessageReceived(object messageRecipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyProviderSelection(valueChangedMessage.Value);
        else DispatcherQueue.TryEnqueue(() => ApplyProviderSelection(valueChangedMessage.Value));
    }

    private async void OnProviderToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isApplyingProviderSelection) return;

        var selectedProviderKind = ProviderToggleSwitch.IsOn ? CliProviderKind.ClaudeCode : CliProviderKind.Codex;
        if (App.ApplicationSettings.SelectedProviderKind == selectedProviderKind) return;

        App.ApplicationSettings.SelectedProviderKind = selectedProviderKind;
        await App.ApplicationSettingsService.SaveSettingsAsync();
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<CliProviderKind>(selectedProviderKind));
    }

    private void RefreshLocalizedText()
    {
        AutomationProperties.SetName(ProviderToggleSwitch, App.LocalizationService.GetLocalizedString("ProviderToggle_AutomationName"));
    }

    private void ApplyProviderSelection(CliProviderKind selectedProviderKind)
    {
        _isApplyingProviderSelection = true;
        try
        {
            ProviderToggleSwitch.IsOn = selectedProviderKind == CliProviderKind.ClaudeCode;
            ProviderTextBlock.Text = GetProviderDisplayName(selectedProviderKind);
        }
        finally
        {
            _isApplyingProviderSelection = false;
        }
    }

    private static string GetProviderDisplayName(CliProviderKind selectedProviderKind)
        => selectedProviderKind switch
        {
            CliProviderKind.ClaudeCode => "Claude Code",
            _ => "Codex"
        };

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action)
    {
        ShowLoading(loadingMessage);
        try
        {
            await action();
            ViewModel.ReloadDashboard();
        }
        finally
        {
            HideLoading();
        }
    }

    private void OnPopupWindowClosed(object sender, WindowEventArgs windowEventArguments) => Dispose();
}
