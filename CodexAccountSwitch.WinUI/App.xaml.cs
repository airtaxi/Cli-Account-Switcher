using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace CodexAccountSwitch.WinUI;

public partial class App : Application
{
    private Window _window;
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
        _window = new MainWindow();
        _window.Activate();
        ApplyPendingNotificationNavigationSection();
        _ = StartupRegistrationService.SetStartupLaunchEnabledAsync(ApplicationSettings.IsStartupLaunchEnabled);
    }

    private void RegisterAppNotificationManager()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) return;

            AppNotificationManager.Default.NotificationInvoked += OnAppNotificationManagerNotificationInvoked;
            AppNotificationManager.Default.Register();
            ProcessLaunchActivationArguments();
        }
        catch { }
    }

    private void ProcessLaunchActivationArguments()
    {
        var appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (appActivationArguments?.Kind != ExtendedActivationKind.AppNotification) return;
        if (appActivationArguments.Data is AppNotificationActivatedEventArgs appNotificationActivatedEventArguments) ProcessAppNotificationActivatedEventArguments(appNotificationActivatedEventArguments);
    }

    private void OnAppNotificationManagerNotificationInvoked(AppNotificationManager appNotificationManager, AppNotificationActivatedEventArgs appNotificationActivatedEventArguments) => ProcessAppNotificationActivatedEventArguments(appNotificationActivatedEventArguments);

    private void ProcessAppNotificationActivatedEventArguments(AppNotificationActivatedEventArgs appNotificationActivatedEventArguments)
    {
        if (!TryGetNotificationAction(appNotificationActivatedEventArguments, out var notificationAction)) return;
        if (string.Equals(notificationAction, ApplicationNotificationService.AccountsNavigationNotificationAction, StringComparison.Ordinal)) NavigateToMainPageSection(MainPageNavigationSection.Accounts);
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

    private static bool TryGetNotificationAction(AppNotificationActivatedEventArgs appNotificationActivatedEventArguments, out string notificationAction)
    {
        var hasNotificationAction = appNotificationActivatedEventArguments.Arguments.TryGetValue(ApplicationNotificationService.NotificationActionArgumentName, out notificationAction);
        notificationAction ??= "";
        return hasNotificationAction && !string.IsNullOrWhiteSpace(notificationAction);
    }
}
