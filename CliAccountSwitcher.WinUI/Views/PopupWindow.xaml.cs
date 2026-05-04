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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using WinUIEx;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class PopupWindow : WindowEx, IDisposable
{
    private const double WindowContentWidth = 480;
    private const double WindowHeightPadding = 10;
    private const int FallbackTaskbarIconOffset = 24;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint nativePoint);

    private int _lastResizedHeight;
    private NativePoint? _taskbarIconAnchorPoint;
    private bool _isResizing;
    private bool _isApplyingProviderSelection;
    private bool _hasQueuedWindowContentResize;
    private bool _disposed;

    public PopupWindow()
    {
        InitializeComponent();

        this.SetIsAlwaysOnTop(true);
        AppWindow.IsShownInSwitchers = false;

        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter) overlappedPresenter.SetBorderAndTitleBar(true, false);

        ViewModel = new DashboardPageViewModel(App.AccountServiceManager, App.ApplicationSettings, DispatcherQueue);
        ViewModel.PropertyChanged += OnDashboardPageViewModelPropertyChanged;

        App.ApplicationThemeService.ApplyThemeToWindow(this);
        WindowInteractionHelper.DisableWindowAnimations(this);
        if (Content is FrameworkElement { RequestedTheme: ElementTheme.Dark }) WindowInteractionHelper.SetDarkModeWindow(this);

        App.ApplicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
        App.LocalizationService.LanguageChanged += RefreshLocalizedText;
        WeakReferenceMessenger.Default.Register<ActiveAccountQuotaRefreshRequestedMessage>(this, OnActiveAccountQuotaRefreshRequestedMessageReceived);
        ApplyProviderSelection(App.ApplicationSettings.SelectedProviderKind);
        RefreshLocalizedText();
    }

    private void OnRootFrameLoaded(object sender, RoutedEventArgs routedEventArguments)
    {
        MoveAndResizeAboveTaskbarIcon();
        MoveAndResizeAboveTaskbarIcon(); // Call twice to ensure usage control to fit the window content size after the first call

        Activate();
    }

    public DashboardPageViewModel ViewModel { get; }

    public void MoveAndResizeAboveTaskbarIcon()
    {
        ResizeWindowToContent(RootGrid, true);
        MoveAboveTaskbarIcon();
        WindowInteractionHelper.ForceForegroundWindow(this);
    }

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
        ViewModel.PropertyChanged -= OnDashboardPageViewModelPropertyChanged;
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool ResizeWindowToContent(FrameworkElement element, bool shouldUpdateMeasure)
    {
        if (_isResizing || element is null) return false;
        _isResizing = true;

        try
        {
            if (shouldUpdateMeasure)
            {
                element.InvalidateMeasure();
                element.Measure(new Size(WindowContentWidth, double.PositiveInfinity));
            }

            var height = (int)Math.Ceiling(element.DesiredSize.Height);
            if (height <= 0 || height == _lastResizedHeight) return false;
            _lastResizedHeight = height;

            var targetHeight = height + WindowHeightPadding;
            Width = WindowContentWidth;
            Height = targetHeight;
            this.SetWindowSize(WindowContentWidth, targetHeight);
            return true;
        }
        finally { _isResizing = false; }
    }

    private void MoveAboveTaskbarIcon()
    {
        var rasterizationScale = (double)this.GetDpiForWindow() / 96;
        var windowWidth = Width * rasterizationScale;
        var windowHeight = Height * rasterizationScale;
        var taskbarRectangle = TaskbarHelper.GetTaskbarRectangle();
        var taskbarPosition = TaskbarHelper.GetTaskbarPosition();
        var taskbarIconAnchorPoint = _taskbarIconAnchorPoint ??= GetTaskbarIconAnchorPoint(taskbarRectangle);

        var positionX = taskbarIconAnchorPoint.X - (windowWidth / 2);
        var positionY = taskbarRectangle.Top - windowHeight;

        switch (taskbarPosition)
        {
            case TaskbarPosition.Top:
                positionX = taskbarIconAnchorPoint.X - (windowWidth / 2);
                positionY = taskbarRectangle.Bottom;
                break;

            case TaskbarPosition.Left:
                positionX = taskbarRectangle.Right;
                positionY = taskbarIconAnchorPoint.Y - (windowHeight / 2);
                break;

            case TaskbarPosition.Right:
                positionX = taskbarRectangle.Left - windowWidth;
                positionY = taskbarIconAnchorPoint.Y - (windowHeight / 2);
                break;
        }

        if (taskbarPosition is TaskbarPosition.Top or TaskbarPosition.Bottom) positionX = ClampToRange(positionX, taskbarRectangle.Left, taskbarRectangle.Right - windowWidth);
        else positionY = ClampToRange(positionY, taskbarRectangle.Top, taskbarRectangle.Bottom - windowHeight);

        this.Move((int)Math.Round(positionX), (int)Math.Round(positionY));
    }

    private static NativePoint GetTaskbarIconAnchorPoint(NativeRectangle taskbarRectangle)
    {
        if (GetCursorPos(out var cursorPoint) && IsPointInsideRectangle(cursorPoint, taskbarRectangle)) return cursorPoint;

        return new NativePoint
        {
            X = taskbarRectangle.Right - FallbackTaskbarIconOffset,
            Y = taskbarRectangle.Bottom - FallbackTaskbarIconOffset
        };
    }

    private static bool IsPointInsideRectangle(NativePoint nativePoint, NativeRectangle nativeRectangle) => nativePoint.X >= nativeRectangle.Left && nativePoint.X <= nativeRectangle.Right && nativePoint.Y >= nativeRectangle.Top && nativePoint.Y <= nativeRectangle.Bottom;

    private static double ClampToRange(double value, double minimum, double maximum)
    {
        if (maximum < minimum) return minimum;
        return Math.Clamp(value, minimum, maximum);
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

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToWindow(this);

    private void OnDashboardPageViewModelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments) => QueueWindowContentResize();

    private async void OnActiveAccountQuotaRefreshRequestedMessageReceived(object messageRecipient, ActiveAccountQuotaRefreshRequestedMessage activeAccountQuotaRefreshRequestedMessage) => await RunWithLoadingAsync(App.LocalizationService.GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage"), async () => await ViewModel.RefreshActiveProviderAccountAsync());

    private async void OnProviderToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isApplyingProviderSelection) return;

        var selectedProviderKind = ProviderToggleSwitch.IsOn ? CliProviderKind.ClaudeCode : CliProviderKind.Codex;
        if (App.ApplicationSettings.SelectedProviderKind == selectedProviderKind) return;

        App.ApplicationSettings.SelectedProviderKind = selectedProviderKind;
        ApplyProviderSelection(selectedProviderKind);
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

    private void QueueWindowContentResize()
    {
        if (_disposed || _hasQueuedWindowContentResize) return;

        _hasQueuedWindowContentResize = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _hasQueuedWindowContentResize = false;
            if (_disposed) return;

            ContentGrid.InvalidateMeasure();
            RootGrid.InvalidateMeasure();
            if (ResizeWindowToContent(ContentGrid, true)) MoveAboveTaskbarIcon();
        });
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
