using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Pages;
using CodexAccountSwitch.WinUI.Services;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace CodexAccountSwitch.WinUI;

public sealed partial class MainWindow : WindowEx
{
    private bool _isApplyingNavigationSelection;

    public static MainWindow Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<MainPageNavigationSection>>(this, OnMainPageNavigationSectionChangedMessageReceived);

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

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToWindow(this);

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
        App.ApplicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        App.LocalizationService.LanguageChanged -= RefreshLocalizedText;
        App.StoreUpdateService.Dispose();
        App.CodexAccountService.Dispose();
    }
}
