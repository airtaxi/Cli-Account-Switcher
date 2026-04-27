using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml;

namespace CodexAccountSwitch.WinUI;

public partial class App : Application
{
    private Window _window;

    // Services
    public static ApplicationSettingsService ApplicationSettingsService { get; private set; }

    public static ApplicationSettings ApplicationSettings => ApplicationSettingsService.Settings;

    public static LocalizationService LocalizationService { get; private set; }

    public static CodexAccountService CodexAccountService { get; private set; }

    public App()
    {
        InitializeComponent();

        // Initialize services
        ApplicationSettingsService = new ApplicationSettingsService();
        LocalizationService = new LocalizationService(ApplicationSettings.LanguageOverride);
        CodexAccountService = new CodexAccountService();
        CodexAccountService.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs launchActivatedEventArguments)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
