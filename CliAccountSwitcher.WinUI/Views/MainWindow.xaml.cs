using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Pages;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.InteropServices;
using WinRT.Interop;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class MainWindow : WindowEx
{
    private const uint WindowsMessageClose = 0x0010;
    private const uint WindowsMessageQueryEndSession = 0x0011;
    private const uint WindowsMessageEndSession = 0x0016;
    private const nuint MainWindowSubclassIdentifier = 1;

    private delegate nint WindowSubclassProcedure(nint windowHandle, uint message, nint messageWordParameter, nint messageLongParameter, nuint subclassIdentifier, nuint referenceData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint windowHandle, WindowSubclassProcedure windowSubclassProcedure, nuint subclassIdentifier, nuint referenceData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint windowHandle, uint message, nint messageWordParameter, nint messageLongParameter);

    private readonly WindowSubclassProcedure _windowSubclassProcedure;
    private PopupWindow _activeAccountQuotaPopupWindow;
    private bool _isApplyingNavigationSelection;
    private bool _isApplyingProviderSelection;
    private bool _isSystemShutdownInProgress;

    public static MainWindow Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // When the app is launched by StartupTask, the window is not activated, so tray commands are wired in code instead of XAML.
        OpenAccountsPageMenuFlyoutItem.Command = OpenAccountsPageCommand;
        OpenDashboardPageMenuFlyoutItem.Command = OpenDashboardPageCommand;
        TaskbarIcon.LeftClickCommand = OpenActiveAccountQuotaPopupCommand;

        _windowSubclassProcedure = WindowSubclassProc;
        SetWindowSubclass(this.GetWindowHandle(), _windowSubclassProcedure, MainWindowSubclassIdentifier, 0);

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<MainPageNavigationSection>>(this, OnMainPageNavigationSectionChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ApplyProviderSelection(App.ApplicationSettings.SelectedProviderKind);

        App.ApplicationThemeService.ApplyThemeToWindow(this);
        App.ApplicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;

        this.CenterOnScreen();
        AppFrame.Navigate(typeof(MainPage));

        RefreshLocalizedText();
        App.LocalizationService.LanguageChanged += RefreshLocalizedText;
    }

    public static void ShowLoading(string message = null)
    {
        if (Instance.DispatcherQueue.HasThreadAccess) ShowLoadingCore(message);
        else Instance.DispatcherQueue.TryEnqueue(() => ShowLoadingCore(message));
    }

    public static void HideLoading()
    {
        if (Instance.DispatcherQueue.HasThreadAccess) HideLoadingCore();
        else Instance.DispatcherQueue.TryEnqueue(HideLoadingCore);
    }

    public static void NavigateToMainPageSection(MainPageNavigationSection mainPageNavigationSection)
    {
        if (Instance is null) return;
        if (Instance.DispatcherQueue.HasThreadAccess) Instance.NavigateToMainPageSectionCore(mainPageNavigationSection);
        else Instance.DispatcherQueue.TryEnqueue(() => Instance.NavigateToMainPageSectionCore(mainPageNavigationSection));
    }

    private static void ShowLoadingCore(string message)
    {
        Instance.AppFrame.IsEnabled = false;

        if (string.IsNullOrWhiteSpace(message)) Instance.LoadingTextBlock.Visibility = Visibility.Collapsed;
        else
        {
            Instance.LoadingTextBlock.Visibility = Visibility.Visible;
            Instance.LoadingTextBlock.Text = message;
        }

        Instance.LoadingGrid.Visibility = Visibility.Visible;
    }

    private static void HideLoadingCore()
    {
        Instance.LoadingGrid.Visibility = Visibility.Collapsed;
        Instance.AppFrame.IsEnabled = true;
    }

    private void RefreshLocalizedText()
    {
        var localizedWindowTitle = App.LocalizationService.GetLocalizedString("MainWindow_AppTitleBar/Title");
        Title = localizedWindowTitle;
        AppTitleBar.Title = localizedWindowTitle;

        DashboardSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_DashboardSelectorBarItem/Text");
        AccountsSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_AccountsSelectorBarItem/Text");
        AboutSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_AboutSelectorBarItem/Text");
        SettingsSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_SettingsSelectorBarItem/Text");
        TaskbarIcon.ToolTipText = App.LocalizationService.GetLocalizedString("AppDisplayName");
        TrayAuthorMenuFlyoutItem.Text = App.LocalizationService.GetLocalizedString("MainWindow_TrayAuthorMenuFlyoutItem/Text");
        OpenDashboardPageMenuFlyoutItem.Text = App.LocalizationService.GetLocalizedString("MainWindow_OpenDashboardPageMenuFlyoutItem/Text");
        OpenAccountsPageMenuFlyoutItem.Text = App.LocalizationService.GetLocalizedString("MainWindow_OpenAccountsPageMenuFlyoutItem/Text");
        CloseProgramMenuFlyoutItem.Text = App.LocalizationService.GetLocalizedString("MainWindow_CloseProgramMenuFlyoutItem/Text");
        AutomationProperties.SetName(ProviderToggleSwitch, App.LocalizationService.GetLocalizedString("ProviderToggle_AutomationName"));
    }

    private void OnAppFrameNavigated(object sender, NavigationEventArgs navigationEventArguments)
    {
        var frame = sender as Frame;

        AppTitleBar.IsBackButtonVisible = frame?.CanGoBack == true;
    }

    private void OnAppTitleBarBackRequested(TitleBar sender, object eventArguments)
    {
        if (AppFrame.CanGoBack) AppFrame.GoBack();
    }

    private void OnPageSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments)
    {
        if (_isApplyingNavigationSelection) return;
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(GetSelectedNavigationSection(sender.SelectedItem)));
    }

    private void OnMainPageNavigationSectionChangedMessageReceived(object messageRecipient, ValueChangedMessage<MainPageNavigationSection> valueChangedMessage) => SelectMainPageNavigationSection(valueChangedMessage.Value);

    private void OnProviderKindChangedMessageReceived(object messageRecipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyProviderSelection(valueChangedMessage.Value);
        else DispatcherQueue.TryEnqueue(() => ApplyProviderSelection(valueChangedMessage.Value));
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToWindow(this);

    private async void OnProviderToggleSwitchToggled(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_isApplyingProviderSelection) return;

        var selectedProviderKind = ProviderToggleSwitch.IsOn ? CliProviderKind.ClaudeCode : CliProviderKind.Codex;
        if (App.ApplicationSettings.SelectedProviderKind == selectedProviderKind) return;

        App.ApplicationSettings.SelectedProviderKind = selectedProviderKind;
        await App.ApplicationSettingsService.SaveSettingsAsync();
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<CliProviderKind>(selectedProviderKind));
    }

    [RelayCommand]
    private void OpenDashboardPage() => NavigateToMainPageSection(MainPageNavigationSection.Dashboard);

    [RelayCommand]
    private void OpenAccountsPage() => NavigateToMainPageSection(MainPageNavigationSection.Accounts);

    [RelayCommand]
    private void OpenActiveAccountQuotaPopup()
    {
        _activeAccountQuotaPopupWindow?.Close();

        var activeAccountQuotaPopupWindow = new PopupWindow();
        _activeAccountQuotaPopupWindow = activeAccountQuotaPopupWindow;
        activeAccountQuotaPopupWindow.Closed += OnActiveAccountQuotaPopupWindowClosed;
    }

    private void OnCloseProgramMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArguments) => Environment.Exit(0);

    private void OnActiveAccountQuotaPopupWindowClosed(object sender, WindowEventArgs windowEventArguments)
    {
        if (sender is not PopupWindow activeAccountQuotaPopupWindow) return;

        activeAccountQuotaPopupWindow.Closed -= OnActiveAccountQuotaPopupWindowClosed;
        if (ReferenceEquals(_activeAccountQuotaPopupWindow, activeAccountQuotaPopupWindow)) _activeAccountQuotaPopupWindow = null;
    }

    private nint WindowSubclassProc(nint windowHandle, uint message, nint messageWordParameter, nint messageLongParameter, nuint subclassIdentifier, nuint referenceData)
    {
        switch (message)
        {
            case WindowsMessageQueryEndSession:
                _isSystemShutdownInProgress = true;
                return 1;

            case WindowsMessageEndSession:
                if (messageWordParameter != 0) Environment.Exit(0);
                return 0;

            case WindowsMessageClose:
                if (_isSystemShutdownInProgress) break;
                this.Hide();
                return 0;
        }

        return DefSubclassProc(windowHandle, message, messageWordParameter, messageLongParameter);
    }

    private void NavigateToMainPageSectionCore(MainPageNavigationSection mainPageNavigationSection)
    {
        Activate();
        BringToFront();
        SelectMainPageNavigationSection(mainPageNavigationSection);
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(mainPageNavigationSection));
    }

    private void SelectMainPageNavigationSection(MainPageNavigationSection mainPageNavigationSection)
    {
        var selectorBarItem = GetSelectorBarItem(mainPageNavigationSection);
        if (PageSelectorBar.SelectedItem == selectorBarItem) return;

        _isApplyingNavigationSelection = true;
        try
        {
            PageSelectorBar.SelectedItem = selectorBarItem;
        }
        finally
        {
            _isApplyingNavigationSelection = false;
        }
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

    private SelectorBarItem GetSelectorBarItem(MainPageNavigationSection mainPageNavigationSection) => mainPageNavigationSection switch
    {
        MainPageNavigationSection.Dashboard => DashboardSelectorBarItem,
        MainPageNavigationSection.Accounts => AccountsSelectorBarItem,
        MainPageNavigationSection.About => AboutSelectorBarItem,
        MainPageNavigationSection.Settings => SettingsSelectorBarItem,
        _ => throw new ArgumentOutOfRangeException(nameof(mainPageNavigationSection), mainPageNavigationSection, "Unknown main page navigation section.")
    };

    private static MainPageNavigationSection GetSelectedNavigationSection(SelectorBarItem selectorBarItem) => (selectorBarItem?.Tag as string) switch
    {
        "Dashboard" => MainPageNavigationSection.Dashboard,
        "Accounts" => MainPageNavigationSection.Accounts,
        "About" => MainPageNavigationSection.About,
        "Settings" => MainPageNavigationSection.Settings,
        _ => MainPageNavigationSection.Dashboard
    };

    private void OnMainWindowClosed(object sender, WindowEventArgs windowEventArguments)
    {
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<MainPageNavigationSection>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
        App.ApplicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        App.LocalizationService.LanguageChanged -= RefreshLocalizedText;
        _activeAccountQuotaPopupWindow?.Close();
        App.StoreUpdateService.Dispose();
        App.AccountServiceManager.Dispose();
        App.CodexAccountService.Dispose();
        App.ClaudeAccountService.Dispose();
    }
}
