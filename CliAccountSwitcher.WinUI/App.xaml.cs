using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System.Diagnostics;
using System.Text;
using WinUIEx;

namespace CliAccountSwitcher.WinUI;

public partial class App : Application
{
    private static App s_currentApplication;
    private static MainWindow s_mainWindow;
    private static TaskbarUsageWindow s_taskbarUsageWindow;
    private static bool s_shouldShowMainWindowAfterLaunch;
    private static AppActivationArguments s_pendingApplicationActivationArguments;
    private MainPageNavigationSection? _pendingNotificationNavigationSection;

    // Services — resolved from the DI container
    public static IServiceProvider Services { get; private set; }

    public static TaskbarUsageWindow TaskbarUsageWindow => s_taskbarUsageWindow;

    public App()
    {
        s_currentApplication = this;

        // Configure the DI container FIRST — before InitializeComponent triggers any XAML type loading
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        InitializeComponent();

        // Register unhandled exception handlers
        UnhandledException += OnApplicationUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

        RegisterAppNotificationManager();
        Services.GetRequiredService<StoreUpdateService>().Start();
    }

    private static void ConfigureServices(IServiceCollection serviceCollection)
    {
        // DispatcherQueue singleton — resolved on UI thread for NativeAOT-safe ViewModel injection
        serviceCollection.AddSingleton(_ => DispatcherQueue.GetForCurrentThread());

        // Services with no dependencies
        serviceCollection.AddSingleton<FileLogService>();
        serviceCollection.AddSingleton<ApplicationSettingsService>();
        serviceCollection.AddSingleton<StartupRegistrationService>();
        serviceCollection.AddSingleton<CodexApplicationRestartService>();
        serviceCollection.AddSingleton<ClaudeCodeApplicationRestartService>();
        serviceCollection.AddSingleton<SkillService>();

        // LocalizationService — needs language tag from settings
        serviceCollection.AddSingleton(sp => new LocalizationService(sp.GetRequiredService<ApplicationSettingsService>().Settings.LanguageOverride));

        // ApplicationThemeService — needs initial theme from settings
        serviceCollection.AddSingleton(sp => new ApplicationThemeService(sp.GetRequiredService<ApplicationSettingsService>().Settings.Theme));

        // ApplicationNotificationService — depends on LocalizationService
        serviceCollection.AddSingleton(sp => new ApplicationNotificationService(sp.GetRequiredService<LocalizationService>()));

        // ApplicationSettings model as a forwarded singleton
        serviceCollection.AddSingleton(sp => sp.GetRequiredService<ApplicationSettingsService>().Settings);

        // StoreUpdateService — needs ApplicationSettings model + ApplicationNotificationService
        serviceCollection.AddSingleton(sp => new StoreUpdateService(sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<ApplicationNotificationService>()));

        // Account services — concrete singletons first, then forwarded as IAccountService.
        // DO NOT add AddSingleton<IAccountService, T>() — it would create separate instances.
        // DO NOT use AddSingleton<T>() for types with parameterized constructors — use explicit factory lambdas for NativeAOT safety.
        serviceCollection.AddSingleton(sp => new CodexAccountService(sp.GetRequiredService<ApplicationSettingsService>(), sp.GetRequiredService<ApplicationNotificationService>()));
        serviceCollection.AddSingleton(sp => new ClaudeAccountService(sp.GetRequiredService<ApplicationSettingsService>(), sp.GetRequiredService<ApplicationNotificationService>()));
        serviceCollection.AddSingleton<IAccountService>(sp => sp.GetRequiredService<CodexAccountService>());
        serviceCollection.AddSingleton<IAccountService>(sp => sp.GetRequiredService<ClaudeAccountService>());

        // AccountServiceManager — needs ApplicationSettingsService + all IAccountService implementations
        serviceCollection.AddSingleton(sp => new AccountServiceManager(sp.GetRequiredService<ApplicationSettingsService>(), sp.GetServices<IAccountService>()));

        // ViewModels — transient; explicit factory lambdas for NativeAOT compatibility
        serviceCollection.AddTransient(sp => new DashboardPageViewModel(sp.GetRequiredService<AccountServiceManager>(), sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<LocalizationService>(), sp.GetRequiredService<DispatcherQueue>()));

        serviceCollection.AddTransient(sp => new ActiveAccountQuotaControlViewModel(sp.GetRequiredService<LocalizationService>(), sp.GetRequiredService<DispatcherQueue>()));

        serviceCollection.AddTransient(sp => new TaskbarUsageControlViewModel(sp.GetRequiredService<AccountServiceManager>(), sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<LocalizationService>(), sp.GetRequiredService<DispatcherQueue>()));

        serviceCollection.AddTransient(sp => new AccountsPageViewModel(sp.GetRequiredService<AccountServiceManager>(), sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<LocalizationService>(), sp.GetRequiredService<DispatcherQueue>()));

        serviceCollection.AddTransient(sp => new SettingsPageViewModel(sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<ApplicationSettingsService>(), sp.GetRequiredService<ApplicationThemeService>(), sp.GetRequiredService<StartupRegistrationService>(), sp.GetRequiredService<StoreUpdateService>(), sp.GetRequiredService<FileLogService>(), sp.GetRequiredService<AccountServiceManager>(), sp.GetRequiredService<LocalizationService>()));

        serviceCollection.AddTransient(sp => new SkillsPageViewModel(sp.GetRequiredService<SkillService>(), sp.GetRequiredService<ApplicationSettings>(), sp.GetRequiredService<LocalizationService>(), sp.GetRequiredService<DispatcherQueue>()));
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs launchActivatedEventArguments)
    {
        var accountServiceManager = Services.GetRequiredService<AccountServiceManager>();
        await accountServiceManager.InitializeAsync();
        accountServiceManager.Start();

        var applicationActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();

        s_mainWindow = new MainWindow();
        if (!IsStartupTaskActivation(applicationActivationArguments) || s_shouldShowMainWindowAfterLaunch) ShowMainWindow();
        s_shouldShowMainWindowAfterLaunch = false;

        ApplyPendingNotificationNavigationSection();
        ApplyPendingApplicationActivationArguments();
        var applicationSettings = Services.GetRequiredService<ApplicationSettings>();
        _ = Services.GetRequiredService<StartupRegistrationService>().SetStartupLaunchEnabledAsync(applicationSettings.IsStartupLaunchEnabled);
        if (!applicationSettings.HideTaskbarUsage) await InitializeTaskbarUsageWindowAsync();
    }

    public static void ShowMainWindow()
    {
        if (s_mainWindow is null)
        {
            s_shouldShowMainWindowAfterLaunch = true;
            return;
        }

        s_mainWindow.DispatcherQueue.TryEnqueue(ActivateMainWindow);
    }

    public static async Task InitializeTaskbarUsageWindowAsync()
    {
        if (s_taskbarUsageWindow is not null) return;

        var taskbarUsageWindow = new TaskbarUsageWindow();
        s_taskbarUsageWindow = taskbarUsageWindow;
        taskbarUsageWindow.Closed += OnTaskbarUsageWindowClosed;
        taskbarUsageWindow.TaskbarContentHost.TaskbarWindowRecreated += OnTaskbarContentHostTaskbarWindowRecreated;

        try
        {
            await taskbarUsageWindow.PrepareTaskbarContentAsync();
            taskbarUsageWindow.Activate();
        }
        catch
        {
            ReleaseTaskbarUsageWindow(taskbarUsageWindow);
            taskbarUsageWindow.Close();
            throw;
        }
    }

    public static void CloseTaskbarUsageWindow()
    {
        if (s_taskbarUsageWindow is null) return;

        var taskbarUsageWindow = s_taskbarUsageWindow;
        ReleaseTaskbarUsageWindow(taskbarUsageWindow);
        taskbarUsageWindow.Close();
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

    private static void ActivateMainWindow()
    {
        s_mainWindow.Activate();
        s_mainWindow.BringToFront();
    }

    private static void OnTaskbarUsageWindowClosed(object sender, WindowEventArgs windowEventArguments)
    {
        if (sender is TaskbarUsageWindow taskbarUsageWindow) ReleaseTaskbarUsageWindow(taskbarUsageWindow);
    }

    private static async void OnTaskbarContentHostTaskbarWindowRecreated(object sender, EventArgs eventArguments)
    {
        var taskbarUsageWindow = s_taskbarUsageWindow;
        if (taskbarUsageWindow is not null) ReleaseTaskbarUsageWindow(taskbarUsageWindow);

        try
        {
            await Task.Delay(1000);
            if (Services?.GetService<ApplicationSettings>()?.HideTaskbarUsage == false) await InitializeTaskbarUsageWindowAsync();
        }
        catch (Exception exception) { Services?.GetService<FileLogService>()?.WriteWarning(nameof(App), "Failed to recreate the taskbar usage window.", exception); }
    }

    private static void ReleaseTaskbarUsageWindow(TaskbarUsageWindow taskbarUsageWindow)
    {
        taskbarUsageWindow.Closed -= OnTaskbarUsageWindowClosed;
        taskbarUsageWindow.TaskbarContentHost.TaskbarWindowRecreated -= OnTaskbarContentHostTaskbarWindowRecreated;
        if (ReferenceEquals(s_taskbarUsageWindow, taskbarUsageWindow)) s_taskbarUsageWindow = null;
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

    private static void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs unhandledExceptionEventArguments)
    {
        WriteException("Microsoft.UI.Xaml.Application.UnhandledException", unhandledExceptionEventArguments.Exception);
        unhandledExceptionEventArguments.Handled = true;
    }

    private static void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs unhandledExceptionEventArguments)
    {
        if (unhandledExceptionEventArguments.ExceptionObject is Exception exception) WriteException("AppDomain.CurrentDomain.UnhandledException", exception);
    }

    private static void OnTaskSchedulerUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs unobservedTaskExceptionEventArguments)
    {
        WriteException("TaskScheduler.UnobservedTaskException", unobservedTaskExceptionEventArguments.Exception);
        unobservedTaskExceptionEventArguments.SetObserved();
    }

    private static void WriteException(string source, Exception exception)
    {
        Debug.WriteLine(CreateExceptionMessage(source, exception));
        Services?.GetService<FileLogService>()?.WriteError(nameof(App), $"Unhandled exception reported by {source}.", exception);
    }

    private static string CreateExceptionMessage(string source, Exception exception)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append('[');
        stringBuilder.Append(source);
        stringBuilder.Append("] ");

        var currentException = exception;
        while (currentException is not null)
        {
            stringBuilder.Append(currentException.GetType().FullName);
            stringBuilder.Append(": ");
            stringBuilder.Append(currentException.Message);
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(currentException.StackTrace);

            currentException = currentException.InnerException;
            if (currentException is not null) stringBuilder.AppendLine("--->");
        }

        return stringBuilder.ToString();
    }

    private static bool IsStartupTaskActivation(AppActivationArguments applicationActivationArguments) => applicationActivationArguments?.Kind == ExtendedActivationKind.StartupTask;
}
