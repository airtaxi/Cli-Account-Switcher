using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Views;
using CliAccountSwitcher.WinUI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace CliAccountSwitcher.WinUI.Controls;

public sealed partial class ActiveAccountQuotaControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(DashboardPageViewModel), typeof(ActiveAccountQuotaControl), new PropertyMetadata(null, OnViewModelPropertyChanged));

    public static readonly DependencyProperty ShouldUseMainWindowNavigationProperty = DependencyProperty.Register(nameof(ShouldUseMainWindowNavigation), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ShouldShowCardChromeProperty = DependencyProperty.Register(nameof(ShouldShowCardChrome), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(true, OnShouldShowCardChromePropertyChanged));

    public static readonly DependencyProperty ShouldShowRefreshButtonProperty = DependencyProperty.Register(nameof(ShouldShowRefreshButton), typeof(bool), typeof(ActiveAccountQuotaControl), new PropertyMetadata(false, OnShouldShowRefreshButtonPropertyChanged));

    private bool _hasInitializedComponent;

    public ActiveAccountQuotaControl()
    {
        InitializeComponent();
        _hasInitializedComponent = true;
        UpdateCardChrome();
        Unloaded += OnActiveAccountQuotaControlUnloaded;
    }

    public DashboardPageViewModel ViewModel
    {
        get => (DashboardPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
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

    public int ManageAccountsButtonColumnSpan => ShouldShowRefreshButton ? 1 : 2;

    public bool HasActiveAccount => ViewModel?.HasActiveAccount == true;

    public bool HasNoActiveAccount => ViewModel?.HasNoActiveAccount != false;

    public string ActiveAccountDisplayNameText => ViewModel?.ActiveAccountDisplayNameText ?? "";

    public string ActiveAccountEmailAddressText => ViewModel?.ActiveAccountEmailAddressText ?? "";

    public string ActiveAccountPlanText => ViewModel?.ActiveAccountPlanText ?? "";

    public string ActiveAccountPrimaryUsageRemainingText => ViewModel?.ActiveAccountPrimaryUsageRemainingText ?? "";

    public string ActiveAccountSecondaryUsageRemainingText => ViewModel?.ActiveAccountSecondaryUsageRemainingText ?? "";

    public int ActiveAccountPrimaryUsageRemainingPercentage => ViewModel?.ActiveAccountPrimaryUsageRemainingPercentage ?? 0;

    public int ActiveAccountSecondaryUsageRemainingPercentage => ViewModel?.ActiveAccountSecondaryUsageRemainingPercentage ?? 0;

    public string ActiveAccountLastUsageRefreshText => ViewModel?.ActiveAccountLastUsageRefreshText ?? "";

    public bool IsActiveAccountPrimaryUsageUnderWarningThreshold => ViewModel?.IsActiveAccountPrimaryUsageUnderWarningThreshold == true;

    public bool IsActiveAccountSecondaryUsageUnderWarningThreshold => ViewModel?.IsActiveAccountSecondaryUsageUnderWarningThreshold == true;

    private static void OnViewModelPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArguments)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;

        if (dependencyPropertyChangedEventArguments.OldValue is DashboardPageViewModel oldDashboardPageViewModel) oldDashboardPageViewModel.PropertyChanged -= activeAccountQuotaControl.OnDashboardPageViewModelPropertyChanged;
        if (dependencyPropertyChangedEventArguments.NewValue is DashboardPageViewModel newDashboardPageViewModel) newDashboardPageViewModel.PropertyChanged += activeAccountQuotaControl.OnDashboardPageViewModelPropertyChanged;

        activeAccountQuotaControl.RefreshBindings();
    }

    private static void OnShouldShowCardChromePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArguments)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        activeAccountQuotaControl.UpdateCardChrome();
    }

    private static void OnShouldShowRefreshButtonPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArguments)
    {
        var activeAccountQuotaControl = (ActiveAccountQuotaControl)dependencyObject;
        activeAccountQuotaControl.RefreshBindings();
    }

    private void OnDashboardPageViewModelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (DispatcherQueue.HasThreadAccess) RefreshBindings();
        else DispatcherQueue.TryEnqueue(RefreshBindings);
    }

    private void OnManageAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (ShouldUseMainWindowNavigation)
        {
            MainWindow.NavigateToMainPageSection(MainPageNavigationSection.Accounts);
            return;
        }

        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Accounts));
    }

    private void OnRefreshActiveAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments) => WeakReferenceMessenger.Default.Send(new ActiveAccountQuotaRefreshRequestedMessage());

    private void OnActiveAccountQuotaControlUnloaded(object sender, RoutedEventArgs routedEventArguments)
    {
        if (ViewModel is not null) ViewModel.PropertyChanged -= OnDashboardPageViewModelPropertyChanged;
    }

    private void RefreshBindings()
    {
        if (!_hasInitializedComponent) return;
        Bindings.Update();
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
}
