using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Pages;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
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
    private const int CodexProviderSelectedIndex = 0;
    private const int ClaudeCodeProviderSelectedIndex = 1;
    private const int ZaiProviderSelectedIndex = 2;
    private const int OpenCodeGoProviderSelectedIndex = 3;
    private const int OllamaProviderSelectedIndex = 4;

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

    private readonly ApplicationSettingsService _applicationSettingsService = App.Services.GetRequiredService<ApplicationSettingsService>();
    private readonly ApplicationSettings _applicationSettings = App.Services.GetRequiredService<ApplicationSettings>();
    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();

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
        OpenSkillsPageMenuFlyoutItem.Command = OpenSkillsPageCommand;
        TaskbarIcon.LeftClickCommand = OpenActiveAccountQuotaPopupCommand;

        _windowSubclassProcedure = WindowSubclassProc;
        SetWindowSubclass(this.GetWindowHandle(), _windowSubclassProcedure, MainWindowSubclassIdentifier, 0);

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<MainPageNavigationSection>>(this, OnMainPageNavigationSectionChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ApplyProviderSelection(_applicationSettings.SelectedProviderKind);

        _applicationThemeService.ApplyThemeToWindow(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;

        this.CenterOnScreen();
        AppFrame.Navigate(typeof(MainPage));

        RefreshLocalizedText();
        _localizationService.LanguageChanged += RefreshLocalizedText;
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

    public static void ShowActiveAccountQuotaPopup()
    {
        if (Instance is null) return;
        if (Instance.DispatcherQueue.HasThreadAccess) Instance.OpenActiveAccountQuotaPopup();
        else Instance.DispatcherQueue.TryEnqueue(Instance.OpenActiveAccountQuotaPopup);
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
        var localizedWindowTitle = _localizationService.GetLocalizedString("MainWindow_AppTitleBar/Title");
        Title = localizedWindowTitle;
        AppTitleBar.Title = localizedWindowTitle;

        DashboardSelectorBarItem.Text = _localizationService.GetLocalizedString("MainPage_DashboardSelectorBarItem/Text");
        SkillsSelectorBarItem.Text = _localizationService.GetLocalizedString("MainPage_SkillsSelectorBarItem/Text");
        AccountsSelectorBarItem.Text = _localizationService.GetLocalizedString("MainPage_AccountsSelectorBarItem/Text");
        AboutSelectorBarItem.Text = _localizationService.GetLocalizedString("MainPage_AboutSelectorBarItem/Text");
        SettingsSelectorBarItem.Text = _localizationService.GetLocalizedString("MainPage_SettingsSelectorBarItem/Text");
        TaskbarIcon.ToolTipText = _localizationService.GetLocalizedString("AppDisplayName");
        TrayAuthorMenuFlyoutItem.Text = _localizationService.GetLocalizedString("MainWindow_TrayAuthorMenuFlyoutItem/Text");
        OpenDashboardPageMenuFlyoutItem.Text = _localizationService.GetLocalizedString("MainWindow_OpenDashboardPageMenuFlyoutItem/Text");
        OpenAccountsPageMenuFlyoutItem.Text = _localizationService.GetLocalizedString("MainWindow_OpenAccountsPageMenuFlyoutItem/Text");
        OpenSkillsPageMenuFlyoutItem.Text = _localizationService.GetLocalizedString("MainWindow_OpenSkillsPageMenuFlyoutItem/Text");
        CloseProgramMenuFlyoutItem.Text = _localizationService.GetLocalizedString("MainWindow_CloseProgramMenuFlyoutItem/Text");
        AutomationProperties.SetName(ProviderComboBox, _localizationService.GetLocalizedString("ProviderComboBox_AutomationName"));
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

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToWindow(this);

    private async void OnProviderComboBoxSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArguments)
    {
        if (_isApplyingProviderSelection) return;

        var selectedProviderKind = GetProviderKindFromSelectedIndex(ProviderComboBox.SelectedIndex);
        if (_applicationSettings.SelectedProviderKind == selectedProviderKind) return;

        _applicationSettings.SelectedProviderKind = selectedProviderKind;
        await _applicationSettingsService.SaveSettingsAsync();
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<CliProviderKind>(selectedProviderKind));
    }

    [RelayCommand]
    private void OpenDashboardPage() => NavigateToMainPageSection(MainPageNavigationSection.Dashboard);

    [RelayCommand]
    private void OpenAccountsPage() => NavigateToMainPageSection(MainPageNavigationSection.Accounts);

    [RelayCommand]
    private void OpenSkillsPage() => NavigateToMainPageSection(MainPageNavigationSection.Skills);

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
        try { PageSelectorBar.SelectedItem = selectorBarItem; }
        finally { _isApplyingNavigationSelection = false; }
    }

    private void ApplyProviderSelection(CliProviderKind selectedProviderKind)
    {
        _isApplyingProviderSelection = true;
        try { ProviderComboBox.SelectedIndex = GetProviderSelectedIndex(selectedProviderKind); }
        finally { _isApplyingProviderSelection = false; }
    }

    private static int GetProviderSelectedIndex(CliProviderKind selectedProviderKind) => selectedProviderKind switch { CliProviderKind.ClaudeCode => ClaudeCodeProviderSelectedIndex, CliProviderKind.Zai => ZaiProviderSelectedIndex, CliProviderKind.OpenCodeGo => OpenCodeGoProviderSelectedIndex, CliProviderKind.Ollama => OllamaProviderSelectedIndex, _ => CodexProviderSelectedIndex  };

    private static CliProviderKind GetProviderKindFromSelectedIndex(int selectedIndex) => selectedIndex switch { ClaudeCodeProviderSelectedIndex => CliProviderKind.ClaudeCode, ZaiProviderSelectedIndex => CliProviderKind.Zai, OpenCodeGoProviderSelectedIndex => CliProviderKind.OpenCodeGo, OllamaProviderSelectedIndex => CliProviderKind.Ollama, _ => CliProviderKind.Codex  };

    private SelectorBarItem GetSelectorBarItem(MainPageNavigationSection mainPageNavigationSection) => mainPageNavigationSection switch
    {
        MainPageNavigationSection.Dashboard => DashboardSelectorBarItem,
        MainPageNavigationSection.Skills => SkillsSelectorBarItem,
        MainPageNavigationSection.Accounts => AccountsSelectorBarItem,
        MainPageNavigationSection.About => AboutSelectorBarItem,
        MainPageNavigationSection.Settings => SettingsSelectorBarItem,
        _ => throw new ArgumentOutOfRangeException(nameof(mainPageNavigationSection), mainPageNavigationSection, "Unknown main page navigation section.")
    };

    private static MainPageNavigationSection GetSelectedNavigationSection(SelectorBarItem selectorBarItem) => (selectorBarItem?.Tag as string) switch
    {
        "Dashboard" => MainPageNavigationSection.Dashboard,
        "Skills" => MainPageNavigationSection.Skills,
        "Accounts" => MainPageNavigationSection.Accounts,
        "About" => MainPageNavigationSection.About,
        "Settings" => MainPageNavigationSection.Settings,
        _ => MainPageNavigationSection.Dashboard
    };

    private async void OnMainWindowClosed(object sender, WindowEventArgs windowEventArguments)
    {
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<MainPageNavigationSection>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
        _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        _localizationService.LanguageChanged -= RefreshLocalizedText;
        _activeAccountQuotaPopupWindow?.Close();
        App.CloseTaskbarUsageWindow();
        if (App.Services is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
    }
}
