using CliAccountSwitcher.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace CliAccountSwitcher.WinUI.Controls;

public sealed partial class ActiveAccountQuotaControl : UserControl
{
    public static readonly DependencyProperty DashboardViewModelProperty = DependencyProperty.Register(nameof(DashboardViewModel), typeof(DashboardPageViewModel), typeof(ActiveAccountQuotaControl), new PropertyMetadata(null, OnDashboardViewModelPropertyChanged));
    public static readonly DependencyProperty ShouldUseMainWindowNavigationProperty = DependencyProperty.Register(nameof(ShouldUseMainWindowNavigation), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(false, OnShouldUseMainWindowNavigationPropertyChanged));
    public static readonly DependencyProperty ShouldShowCardChromeProperty = DependencyProperty.Register(nameof(ShouldShowCardChrome), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(true, OnShouldShowCardChromePropertyChanged));
    public static readonly DependencyProperty ShouldShowRefreshButtonProperty = DependencyProperty.Register(nameof(ShouldShowRefreshButton), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(false, OnShouldShowRefreshButtonPropertyChanged));

    private DispatcherTimer _remainingTimeRefreshTimer;
    private bool _hasInitializedComponent;
    private bool _isLoaded;

    public ActiveAccountQuotaControlViewModel ViewModel { get; }

    public DashboardPageViewModel DashboardViewModel
    {
        get => (DashboardPageViewModel)GetValue(DashboardViewModelProperty);
        set => SetValue(DashboardViewModelProperty, value);
    }

    public bool ShouldUseMainWindowNavigation
    {
        get => (bool)GetValue(ShouldUseMainWindowNavigationProperty);
        set => SetValue(ShouldUseMainWindowNavigationProperty, value);
    }

    public bool ShouldShowCardChrome
    {
        get => (bool)GetValue(ShouldShowCardChromeProperty);
        set => SetValue(ShouldShowCardChromeProperty, value);
    }

    public bool ShouldShowRefreshButton
    {
        get => (bool)GetValue(ShouldShowRefreshButtonProperty);
        set => SetValue(ShouldShowRefreshButtonProperty, value);
    }

    public ActiveAccountQuotaControl()
    {
        ViewModel = App.Services.GetRequiredService<ActiveAccountQuotaControlViewModel>();

        InitializeComponent();
        _hasInitializedComponent = true;

        SyncViewModelOptions();
        UpdateCardChrome();
    }

    private static void OnDashboardViewModelPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArguments)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        if (activeAccountQuotaControl._isLoaded) activeAccountQuotaControl.ViewModel.DashboardViewModel = dependencyPropertyChangedEventArguments.NewValue as DashboardPageViewModel;
    }

    private static void OnShouldUseMainWindowNavigationPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        activeAccountQuotaControl.ViewModel.ShouldUseMainWindowNavigation = activeAccountQuotaControl.ShouldUseMainWindowNavigation;
    }

    private static void OnShouldShowCardChromePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        activeAccountQuotaControl.UpdateCardChrome();
    }

    private static void OnShouldShowRefreshButtonPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        activeAccountQuotaControl.ViewModel.ShouldShowRefreshButton = activeAccountQuotaControl.ShouldShowRefreshButton;
    }

    private void OnActiveAccountQuotaControlLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ViewModel.DashboardViewModel = DashboardViewModel;
        StartRemainingTimeRefreshTimer();
    }

    private void OnActiveAccountQuotaControlUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        ViewModel.DashboardViewModel = null;
        StopRemainingTimeRefreshTimer();
    }

    private void OnRemainingTimeRefreshTimerTick(object _, object __) => ViewModel.RefreshTimeSensitiveProperties();

    private void StartRemainingTimeRefreshTimer()
    {
        StopRemainingTimeRefreshTimer();

        _remainingTimeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _remainingTimeRefreshTimer.Tick += OnRemainingTimeRefreshTimerTick;
        _remainingTimeRefreshTimer.Start();
        ViewModel.RefreshTimeSensitiveProperties();
    }

    private void StopRemainingTimeRefreshTimer()
    {
        if (_remainingTimeRefreshTimer is null) return;

        _remainingTimeRefreshTimer.Stop();
        _remainingTimeRefreshTimer.Tick -= OnRemainingTimeRefreshTimerTick;
        _remainingTimeRefreshTimer = null;
    }

    private void UpdateCardChrome()
    {
        if (!_hasInitializedComponent) return;

        if (ShouldShowCardChrome)
        {
            ActiveAccountQuotaCardBorder.ClearValue(Border.PaddingProperty);
            ActiveAccountQuotaCardBorder.ClearValue(Border.BackgroundProperty);
            ActiveAccountQuotaCardBorder.ClearValue(Border.BorderBrushProperty);
            ActiveAccountQuotaCardBorder.ClearValue(Border.BorderThicknessProperty);
            return;
        }

        ActiveAccountQuotaCardBorder.Padding = new Thickness(0);
        ActiveAccountQuotaCardBorder.Background = null;
        ActiveAccountQuotaCardBorder.BorderBrush = null;
        ActiveAccountQuotaCardBorder.BorderThickness = new Thickness(0);
    }

    private void SyncViewModelOptions()
    {
        ViewModel.ShouldShowRefreshButton = ShouldShowRefreshButton;
        ViewModel.ShouldUseMainWindowNavigation = ShouldUseMainWindowNavigation;
    }
}
