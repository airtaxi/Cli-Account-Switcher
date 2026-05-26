using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CliAccountSwitcher.WinUI.Managers;

public sealed class AccountServiceManager : IDisposable
{
    private readonly ApplicationSettingsService _applicationSettingsService;
    private readonly IReadOnlyDictionary<CliProviderKind, IAccountService> _accountServicesByKind;
    private readonly CancellationTokenSource _backgroundCancellationTokenSource = new();
    private readonly object _refreshScheduleLock = new();
    private DateTimeOffset? _nextActiveUsageRefreshTime;
    private DateTimeOffset? _nextInactiveUsageRefreshTime;
    private bool _isUsageRefreshScheduleInitialized;
    private bool _disposed;

    public AccountServiceManager(ApplicationSettingsService applicationSettingsService, IEnumerable<IAccountService> accountServices)
    {
        _applicationSettingsService = applicationSettingsService;
        _accountServicesByKind = accountServices.ToDictionary(accountService => accountService.ProviderKind);
        ApplyApplicationSettings();
    }

    public TimeSpan ActiveStatusRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ActiveUsageRefreshInterval { get; private set; } = TimeSpan.FromSeconds(ApplicationSettings.DefaultActiveAccountUsageRefreshIntervalSeconds);

    public TimeSpan InactiveUsageRefreshInterval { get; private set; } = TimeSpan.FromSeconds(ApplicationSettings.DefaultInactiveAccountUsageRefreshIntervalSeconds);

    public bool IsActiveUsageRefreshEnabled { get; private set; } = true;

    public bool IsInactiveUsageRefreshEnabled { get; private set; } = true;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        foreach (var accountService in _accountServicesByKind.Values)
        {
            accountService.AccountsChanged -= OnAccountServiceAccountsChanged;
            accountService.AccountsChanged += OnAccountServiceAccountsChanged;
            await accountService.InitializeAsync(cancellationToken);
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        _applicationSettingsService.SettingsChanged += OnApplicationSettingsServiceSettingsChanged;
        _ = RunActiveStatusRefreshLoopAsync();
        _ = RunActiveUsageRefreshLoopAsync();
        _ = RunInactiveUsageRefreshLoopAsync();
        _ = SynchronizeActiveStatusesSilentlyAsync();
        if (IsActiveUsageRefreshEnabled) _ = RefreshAccountsByActiveStateSilentlyAsync(true);
        if (IsInactiveUsageRefreshEnabled) _ = RefreshAccountsByActiveStateSilentlyAsync(false);
    }

    public IReadOnlyList<ProviderAccount> GetAccounts(CliProviderKind providerKind) => GetAccountService(providerKind).GetAccounts();

    public bool GetIsRenameSupported(CliProviderKind providerKind) => GetAccountService(providerKind).IsRenameSupported;

    public string GetBackupFileNamePrefix(CliProviderKind providerKind) => GetAccountService(providerKind).BackupFileNamePrefix;

    public TimeSpan? GetActiveUsageRefreshRemainingTime()
    {
        lock (_refreshScheduleLock) return GetUsageRefreshRemainingTime(_nextActiveUsageRefreshTime, IsActiveUsageRefreshEnabled);
    }

    public TimeSpan? GetInactiveUsageRefreshRemainingTime()
    {
        lock (_refreshScheduleLock) return GetUsageRefreshRemainingTime(_nextInactiveUsageRefreshTime, IsInactiveUsageRefreshEnabled);
    }

    public async Task RefreshAllAccountsAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).RefreshAllAccountsAsync(cancellationToken);

    public async Task RefreshAccountsAsync(CliProviderKind providerKind, IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).RefreshAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task RefreshAccountAsync(CliProviderKind providerKind, string accountIdentifier, CancellationToken cancellationToken = default) => await RefreshAccountsAsync(providerKind, [accountIdentifier], cancellationToken);

    public async Task RefreshActiveAccountAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default)
    {
        await GetAccountService(providerKind).RefreshActiveAccountAsync(cancellationToken);
        ResetActiveUsageRefreshSchedule();
    }

    public async Task<ProviderActivationFollowUp> ActivateAccountAsync(CliProviderKind providerKind, string accountIdentifier, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).ActivateAccountAsync(accountIdentifier, cancellationToken);

    public async Task DeleteAccountsAsync(CliProviderKind providerKind, IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).DeleteAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task<int> DeleteExpiredAccountsAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).DeleteExpiredAccountsAsync(cancellationToken);

    public async Task RenameAccountAsync(CliProviderKind providerKind, string accountIdentifier, string customAlias, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).RenameAccountAsync(accountIdentifier, customAlias, cancellationToken);

    public async Task ExportBackupAsync(CliProviderKind providerKind, string backupFilePath, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).ExportBackupAsync(backupFilePath, cancellationToken);

    public async Task<ProviderAccountBackupImportResult> ImportBackupAsync(CliProviderKind providerKind, string backupFilePath, CancellationToken cancellationToken = default) => await GetAccountService(providerKind).ImportBackupAsync(backupFilePath, cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backgroundCancellationTokenSource.Cancel();
        _applicationSettingsService.SettingsChanged -= OnApplicationSettingsServiceSettingsChanged;

        foreach (var accountService in _accountServicesByKind.Values) accountService.AccountsChanged -= OnAccountServiceAccountsChanged;

        _backgroundCancellationTokenSource.Dispose();
    }

    private IAccountService GetAccountService(CliProviderKind providerKind)
    {
        if (_accountServicesByKind.TryGetValue(providerKind, out var accountService)) return accountService;
        throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, "Unknown provider kind.");
    }

    private async Task RunActiveStatusRefreshLoopAsync()
    {
        using var periodicTimer = new PeriodicTimer(ActiveStatusRefreshInterval);
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(_backgroundCancellationTokenSource.Token)) await SynchronizeActiveStatusesSilentlyAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunActiveUsageRefreshLoopAsync()
    {
        try
        {
            while (!_backgroundCancellationTokenSource.IsCancellationRequested)
            {
                if (!IsActiveUsageRefreshEnabled)
                {
                    SetNextActiveUsageRefreshTime(null);
                    await Task.Delay(TimeSpan.FromSeconds(1), _backgroundCancellationTokenSource.Token);
                    continue;
                }

                if (!await WaitForNextRefreshAsync(true, ActiveUsageRefreshInterval)) continue;
                await RefreshAccountsByActiveStateSilentlyAsync(true);
                SetNextActiveUsageRefreshTime(DateTimeOffset.UtcNow.Add(ActiveUsageRefreshInterval));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunInactiveUsageRefreshLoopAsync()
    {
        try
        {
            while (!_backgroundCancellationTokenSource.IsCancellationRequested)
            {
                if (!IsInactiveUsageRefreshEnabled)
                {
                    SetNextInactiveUsageRefreshTime(null);
                    await Task.Delay(TimeSpan.FromSeconds(1), _backgroundCancellationTokenSource.Token);
                    continue;
                }

                if (!await WaitForNextRefreshAsync(false, InactiveUsageRefreshInterval)) continue;
                await RefreshAccountsByActiveStateSilentlyAsync(false);
                SetNextInactiveUsageRefreshTime(DateTimeOffset.UtcNow.Add(InactiveUsageRefreshInterval));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> WaitForNextRefreshAsync(bool isActiveAccountRefresh, TimeSpan refreshInterval)
    {
        var nextRefreshTime = GetNextUsageRefreshTime(isActiveAccountRefresh);
        if (nextRefreshTime is null)
        {
            SetNextUsageRefreshTime(isActiveAccountRefresh, DateTimeOffset.UtcNow.Add(refreshInterval));
            return false;
        }

        var remainingTime = nextRefreshTime.Value - DateTimeOffset.UtcNow;
        if (remainingTime <= TimeSpan.Zero) return true;

        await Task.Delay(GetRefreshSchedulePollingDelay(remainingTime), _backgroundCancellationTokenSource.Token);
        return false;
    }

    private async Task SynchronizeActiveStatusesSilentlyAsync()
    {
        foreach (var accountService in _accountServicesByKind.Values)
        {
            try { await accountService.SynchronizeActiveStatusesAsync(_backgroundCancellationTokenSource.Token); }
            catch { }
        }
    }

    private async Task RefreshAccountsByActiveStateSilentlyAsync(bool isActive)
    {
        foreach (var accountService in _accountServicesByKind.Values)
        {
            try { await accountService.RefreshAccountsByActiveStateAsync(isActive, _backgroundCancellationTokenSource.Token); }
            catch { }
        }
    }

    private void ApplyApplicationSettings()
    {
        var applicationSettings = _applicationSettingsService.Settings;
        var activeUsageRefreshInterval = TimeSpan.FromSeconds(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds);
        var inactiveUsageRefreshInterval = TimeSpan.FromSeconds(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds);
        var isActiveUsageRefreshEnabled = applicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;
        var isInactiveUsageRefreshEnabled = applicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;
        var shouldResetUsageRefreshSchedules = !_isUsageRefreshScheduleInitialized || ActiveUsageRefreshInterval != activeUsageRefreshInterval || InactiveUsageRefreshInterval != inactiveUsageRefreshInterval || IsActiveUsageRefreshEnabled != isActiveUsageRefreshEnabled || IsInactiveUsageRefreshEnabled != isInactiveUsageRefreshEnabled;

        ActiveUsageRefreshInterval = activeUsageRefreshInterval;
        InactiveUsageRefreshInterval = inactiveUsageRefreshInterval;
        IsActiveUsageRefreshEnabled = isActiveUsageRefreshEnabled;
        IsInactiveUsageRefreshEnabled = isInactiveUsageRefreshEnabled;
        if (!shouldResetUsageRefreshSchedules) return;

        ResetUsageRefreshSchedules();
        _isUsageRefreshScheduleInitialized = true;
    }

    private void ResetUsageRefreshSchedules()
    {
        var currentTime = DateTimeOffset.UtcNow;
        SetNextActiveUsageRefreshTime(IsActiveUsageRefreshEnabled ? currentTime.Add(ActiveUsageRefreshInterval) : null);
        SetNextInactiveUsageRefreshTime(IsInactiveUsageRefreshEnabled ? currentTime.Add(InactiveUsageRefreshInterval) : null);
    }

    private void ResetActiveUsageRefreshSchedule() => SetNextActiveUsageRefreshTime(IsActiveUsageRefreshEnabled ? DateTimeOffset.UtcNow.Add(ActiveUsageRefreshInterval) : null);

    private DateTimeOffset? GetNextUsageRefreshTime(bool isActiveAccountRefresh)
    {
        lock (_refreshScheduleLock) return isActiveAccountRefresh ? _nextActiveUsageRefreshTime : _nextInactiveUsageRefreshTime;
    }

    private void SetNextUsageRefreshTime(bool isActiveAccountRefresh, DateTimeOffset? nextRefreshTime)
    {
        if (isActiveAccountRefresh) SetNextActiveUsageRefreshTime(nextRefreshTime);
        else SetNextInactiveUsageRefreshTime(nextRefreshTime);
    }

    private void SetNextActiveUsageRefreshTime(DateTimeOffset? nextRefreshTime)
    {
        lock (_refreshScheduleLock) _nextActiveUsageRefreshTime = nextRefreshTime;
    }

    private void SetNextInactiveUsageRefreshTime(DateTimeOffset? nextRefreshTime)
    {
        lock (_refreshScheduleLock) _nextInactiveUsageRefreshTime = nextRefreshTime;
    }

    private TimeSpan? GetUsageRefreshRemainingTime(DateTimeOffset? nextRefreshTime, bool isUsageRefreshEnabled)
    {
        if (!isUsageRefreshEnabled || nextRefreshTime is null) return null;
        var remainingTime = nextRefreshTime.Value - DateTimeOffset.UtcNow;
        return remainingTime <= TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
    }

    private static TimeSpan GetRefreshSchedulePollingDelay(TimeSpan remainingTime) => remainingTime < TimeSpan.FromSeconds(1) ? remainingTime : TimeSpan.FromSeconds(1);

    private void OnApplicationSettingsServiceSettingsChanged(object sender, EventArgs eventArguments) => ApplyApplicationSettings();

    private static void OnAccountServiceAccountsChanged(object sender, EventArgs eventArguments)
    {
        if (sender is not IAccountService accountService) return;
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<ProviderAccountsChangedMessage>(new ProviderAccountsChangedMessage(accountService.ProviderKind)));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AccountServiceManager));
    }
}
