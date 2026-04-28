using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class StartupRegistrationService
{
    private const string StartupTaskIdentifier = "CliAccountSwitcherStartup";

    public async Task<bool> GetIsStartupLaunchEnabledAsync()
    {
        try
        {
            var startupTask = await GetStartupTaskAsync();
            return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch { return false; }
    }

    public async Task<bool> SetStartupLaunchEnabledAsync(bool isStartupLaunchEnabled)
    {
        try
        {
            var startupTask = await GetStartupTaskAsync();
            if (isStartupLaunchEnabled)
            {
                if (startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy) return true;
                var startupTaskState = await startupTask.RequestEnableAsync();
                return startupTaskState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            if (startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy) startupTask.Disable();
            return true;
        }
        catch { return false; }
    }

    private static async Task<StartupTask> GetStartupTaskAsync() => await StartupTask.GetAsync(StartupTaskIdentifier);
}
