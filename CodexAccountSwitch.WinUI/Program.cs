using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace CodexAccountSwitch.WinUI;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var isRedirectedToCurrentApplicationInstance = TryRedirectToCurrentApplicationInstance();
        if (isRedirectedToCurrentApplicationInstance) return;

        Application.Start(applicationStartCallback =>
        {
            var dispatcherQueueSynchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(dispatcherQueueSynchronizationContext);
            _ = new App();
        });
    }

    private static bool TryRedirectToCurrentApplicationInstance()
    {
        var currentApplicationInstance = AppInstance.FindOrRegisterForKey("CodexAccountSwitchWinUI_SingleInstance");

        if (!currentApplicationInstance.IsCurrent)
        {
            var applicationActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            currentApplicationInstance.RedirectActivationToAsync(applicationActivationArguments).AsTask().Wait();
            return true;
        }

        currentApplicationInstance.Activated += OnApplicationInstanceActivated;
        return false;
    }

    private static void OnApplicationInstanceActivated(object sender, AppActivationArguments applicationActivationArguments) => App.HandleApplicationInstanceActivated(applicationActivationArguments);
}
