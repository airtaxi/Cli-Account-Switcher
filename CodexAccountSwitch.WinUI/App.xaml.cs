using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Services;
using CodexAccountSwitch.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using WinUIEx;

namespace CodexAccountSwitch.WinUI;

public partial class App : Application
{
    private static App s_currentApplication;
    private static MainWindow s_mainWindow;
    private static bool s_shouldShowMainWindowAfterLaunch;
    private static AppActivationArguments s_pendingApplicationActivationArguments;
    private MainPageNavigationSection? _pendingNotificationNavigationSection;

    // Services
    public static ApplicationSettingsService ApplicationSettingsService { get; private set; }

    public static ApplicationSettings ApplicationSettings => ApplicationSettingsService.Settings;

    public static LocalizationService LocalizationService { get; private set; }

    public static ApplicationThemeService ApplicationThemeService { get; private set; }

    public static ApplicationNotificationService ApplicationNotificationService { get; private set; }

    public static StartupRegistrationService StartupRegistrationService { get; private set; }

    public static StoreUpdateService StoreUpdateService { get; private set; }

    public static CodexAccountService CodexAccountService { get; private set; }

    public static CodexApplicationRestartService CodexApplicationRestartService { get; private set; }

    public App()
    {
        s_currentApplication = this;

        InitializeComponent();

        // Initialize services
        ApplicationSettingsService = new ApplicationSettingsService();
        LocalizationService = new LocalizationService(ApplicationSettings.LanguageOverride);
        ApplicationThemeService = new ApplicationThemeService(ApplicationSettings.Theme);
        ApplicationNotificationService = new ApplicationNotificationService();
        StartupRegistrationService = new StartupRegistrationService();
        StoreUpdateService = new StoreUpdateService(ApplicationSettings, ApplicationNotificationService);
        CodexAccountService = new CodexAccountService(ApplicationSettingsService, ApplicationNotificationService);
        CodexApplicationRestartService = new CodexApplicationRestartService();
        RegisterAppNotificationManager();
        StoreUpdateService.Start();
        CodexAccountService.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs launchActivatedEventArguments)
    {
        var applicationActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();

        s_mainWindow = new MainWindow();
        if (!IsStartupTaskActivation(applicationActivationArguments) || s_shouldShowMainWindowAfterLaunch) ShowMainWindow();
        s_shouldShowMainWindowAfterLaunch = false;

        ApplyPendingNotificationNavigationSection();
        ApplyPendingApplicationActivationArguments();
        _ = StartupRegistrationService.SetStartupLaunchEnabledAsync(ApplicationSettings.IsStartupLaunchEnabled);
    }

    public static void ShowMainWindow()
    {
        if (s_mainWindow is null)
        {
            s_shouldShowMainWindowAfterLaunch = true;
            return;
        }

        s_mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            s_mainWindow.Activate();
            s_mainWindow.BringToFront();
        });
    }

    public static void HandleApplicationInstanceActivated(AppActivationArguments applicationActivationArguments)
    {
        if (IsStartupTaskActivation(applicationActivationArguments)) return;

        if (s_currentApplication is null)
        {
            s_pendingApplicationActivationArguments = applicationActivationArguments;
            ShowMainWindow();
            return;
        }

        s_currentApplication.ProcessRedirectedActivationArguments(applicationActivationArguments);
    }

    private void RegisterAppNotificationManager()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) return;

            AppNotificationManager.Default.NotificationInvoked += OnApplicationNotificationManagerNotificationInvoked;
            AppNotificationManager.Default.Register();
            ProcessLaunchActivationArguments();
        }
        catch { }
    }

    private void ProcessLaunchActivationArguments()
    {
        var applicationActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        ProcessNotificationActivationArguments(applicationActivationArguments);
    }

    private void OnApplicationNotificationManagerNotificationInvoked(AppNotificationManager applicationNotificationManager, AppNotificationActivatedEventArgs applicationNotificationActivatedEventArguments)
    {
        ShowMainWindow();
        ProcessApplicationNotificationActivatedEventArguments(applicationNotificationActivatedEventArguments);
    }

    private void ProcessApplicationNotificationActivatedEventArguments(AppNotificationActivatedEventArgs applicationNotificationActivatedEventArguments)
    {
        if (!TryGetNotificationAction(applicationNotificationActivatedEventArguments, out var notificationAction)) return;
        if (string.Equals(notificationAction, ApplicationNotificationService.AccountsNavigationNotificationAction, StringComparison.Ordinal)) NavigateToMainPageSection(MainPageNavigationSection.Accounts);
    }

    private void ProcessRedirectedActivationArguments(AppActivationArguments applicationActivationArguments)
    {
        if (IsStartupTaskActivation(applicationActivationArguments)) return;

        ShowMainWindow();
        ProcessNotificationActivationArguments(applicationActivationArguments);
    }

    private void ProcessNotificationActivationArguments(AppActivationArguments applicationActivationArguments)
    {
        if (applicationActivationArguments?.Kind != ExtendedActivationKind.AppNotification) return;
        if (applicationActivationArguments.Data is AppNotificationActivatedEventArgs applicationNotificationActivatedEventArguments) ProcessApplicationNotificationActivatedEventArguments(applicationNotificationActivatedEventArguments);
    }

    private void NavigateToMainPageSection(MainPageNavigationSection mainPageNavigationSection)
    {
        if (MainWindow.Instance is null)
        {
            _pendingNotificationNavigationSection = mainPageNavigationSection;
            return;
        }

        MainWindow.NavigateToMainPageSection(mainPageNavigationSection);
    }

    private void ApplyPendingNotificationNavigationSection()
    {
        if (_pendingNotificationNavigationSection is null) return;

        var pendingNotificationNavigationSection = _pendingNotificationNavigationSection.Value;
        _pendingNotificationNavigationSection = null;
        MainWindow.NavigateToMainPageSection(pendingNotificationNavigationSection);
    }

    private void ApplyPendingApplicationActivationArguments()
    {
        if (s_pendingApplicationActivationArguments is null) return;

        var pendingApplicationActivationArguments = s_pendingApplicationActivationArguments;
        s_pendingApplicationActivationArguments = null;
        ProcessRedirectedActivationArguments(pendingApplicationActivationArguments);
    }

    private static bool TryGetNotificationAction(AppNotificationActivatedEventArgs applicationNotificationActivatedEventArguments, out string notificationAction)
    {
        var hasNotificationAction = applicationNotificationActivatedEventArguments.Arguments.TryGetValue(ApplicationNotificationService.NotificationActionArgumentName, out notificationAction);
        notificationAction ??= "";
        return hasNotificationAction && !string.IsNullOrWhiteSpace(notificationAction);
    }

    private static bool IsStartupTaskActivation(AppActivationArguments applicationActivationArguments) => applicationActivationArguments?.Kind == ExtendedActivationKind.StartupTask;
}
