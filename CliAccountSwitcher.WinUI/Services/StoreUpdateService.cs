using CliAccountSwitcher.WinUI.Models;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.System;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class StoreUpdateService(ApplicationSettings applicationSettings, ApplicationNotificationService applicationNotificationService) : IDisposable
{
    private const string StorePackageFamilyName = "49536HowonLee.CodexAccountSwitch_q278kdbtfr3f2";
    private const string StoreProductIdentifier = "9N25QV7VTL54";
    private static readonly TimeSpan s_updateCheckInterval = TimeSpan.FromHours(8);
    private static readonly Uri s_storePackageFamilyNameProductPageAddress = new($"ms-windows-store://pdp/?PFN={StorePackageFamilyName}");
    private static readonly Uri s_storeProductIdentifierProductPageAddress = new($"ms-windows-store://pdp/?ProductId={StoreProductIdentifier}");

    private readonly ApplicationSettings _applicationSettings = applicationSettings;
    private readonly ApplicationNotificationService _applicationNotificationService = applicationNotificationService;
    private CancellationTokenSource _updateCheckCancellationTokenSource;
    private bool _isStarted;
    private bool _disposed;

    public string StoreProductIdentifierText => StoreProductIdentifier;

    public string StorePackageFamilyNameText => StorePackageFamilyName;

    public void Start()
    {
        if (_isStarted) return;

        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        SynchronizeMonitoringState();
        _isStarted = true;
    }

    public async Task<int> GetAvailableUpdateCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storeContext = StoreContext.GetDefault();
        var storePackageUpdates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return storePackageUpdates.Count;
    }

    public async Task<bool> OpenStoreProductPageAsync() => await Launcher.LaunchUriAsync(s_storePackageFamilyNameProductPageAddress) || await Launcher.LaunchUriAsync(s_storeProductIdentifierProductPageAddress);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_isStarted) _applicationSettings.PropertyChanged -= OnApplicationSettingsPropertyChanged;
        StopMonitoring();
    }

    private async Task CheckForUpdatesAndNotifyAsync(CancellationToken cancellationToken)
    {
        var availableUpdateCount = await GetAvailableUpdateCountAsync(cancellationToken);
        if (availableUpdateCount > 0) _applicationNotificationService.ShowStoreUpdateAvailableNotification(availableUpdateCount, s_storePackageFamilyNameProductPageAddress);
    }

    private async Task RunPeriodicUpdateCheckLoopAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(s_updateCheckInterval);
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                try { await CheckForUpdatesAndNotifyAsync(cancellationToken); }
                catch (COMException) { }
                catch (InvalidOperationException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StartMonitoring()
    {
        if (_updateCheckCancellationTokenSource is not null) return;

        _updateCheckCancellationTokenSource = new CancellationTokenSource();
        _ = RunPeriodicUpdateCheckLoopAsync(_updateCheckCancellationTokenSource.Token);
    }

    private void StopMonitoring()
    {
        _updateCheckCancellationTokenSource?.Cancel();
        _updateCheckCancellationTokenSource?.Dispose();
        _updateCheckCancellationTokenSource = null;
    }

    private void SynchronizeMonitoringState()
    {
        if (_applicationSettings.IsAutomaticUpdateCheckEnabled)
        {
            StartMonitoring();
            return;
        }

        StopMonitoring();
    }

    private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName != nameof(ApplicationSettings.IsAutomaticUpdateCheckEnabled)) return;
        SynchronizeMonitoringState();
    }
}
