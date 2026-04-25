using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml;

namespace CodexAccountSwitch.WinUI;

public partial class App : Application
{
    private Window _window;

    // Services
    public static LocalizationService LocalizationService { get; private set; }

    public static CodexAccountService CodexAccountService { get; private set; }

    public App()
    {
        InitializeComponent();

        // Initialize services
        LocalizationService = new LocalizationService(string.Empty);
        CodexAccountService = new CodexAccountService();
        CodexAccountService.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
