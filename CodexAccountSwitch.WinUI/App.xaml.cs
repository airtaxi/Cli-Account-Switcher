using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace CodexAccountSwitch.WinUI;

public partial class App : Application
{
    private Window _window;

    // Services
    public static ApplicationSettingsService ApplicationSettingsService { get; private set; }

    public static ApplicationSettings ApplicationSettings => ApplicationSettingsService.Settings;

    public static LocalizationService LocalizationService { get; private set; }

    public static ApplicationThemeService ApplicationThemeService { get; private set; }

    public static ApplicationNotificationService ApplicationNotificationService { get; private set; }

    public static StartupRegistrationService StartupRegistrationService { get; private set; }

    public static StoreUpdateService StoreUpdateService { get; private set; }

    public static CodexAccountService CodexAccountService { get; private set; }

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
        RegisterAppNotificationManager();
        StoreUpdateService.Start();
        CodexAccountService.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs launchActivatedEventArguments)
    {
        _window = new MainWindow();
        _window.Activate();
        _ = StartupRegistrationService.SetStartupLaunchEnabledAsync(ApplicationSettings.IsStartupLaunchEnabled);
    }

    private static void RegisterAppNotificationManager()
    {
        try
        {
            if (AppNotificationManager.IsSupported()) AppNotificationManager.Default.Register();
        }
        catch { }
    }
}
