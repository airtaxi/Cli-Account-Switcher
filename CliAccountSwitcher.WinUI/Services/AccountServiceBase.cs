using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using System.Net;

namespace CliAccountSwitcher.WinUI.Services;

public abstract class AccountServiceBase<TAccountState>(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService) : IAccountService where TAccountState : class
{
    protected readonly ApplicationSettingsService ApplicationSettingsService = applicationSettingsService;
    protected readonly ApplicationNotificationService ApplicationNotificationService = applicationNotificationService;

    private readonly object _accountsLock = new();
    private readonly List<TAccountState> _accounts = [];
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private bool _disposed;

    public abstract CliProviderKind ProviderKind { get; }

    public abstract string BackupFileNamePrefix { get; }

    public abstract bool IsRenameSupported { get; }

    public event EventHandler AccountsChanged;

    public IReadOnlyList<ProviderAccount> GetAccounts()
    {
        lock (_accountsLock)
        {
            return _accounts.Select(CreateProviderAccount).ToArray();
        }
    }

    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var accountStates = await LoadAccountStatesCoreAsync(cancellationToken);
        ReplaceAccountStates(accountStates);
    }

    public virtual async Task SynchronizeActiveStatusesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var activeAccountIdentifier = await ReadActiveAccountIdentifierAsync(cancellationToken);
        var changedAccountStates = SynchronizeActiveStatuses(activeAccountIdentifier);
        if (changedAccountStates.Count == 0) return;

        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public async Task RefreshAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await RefreshAccountStatesAsync(_ => true, cancellationToken);
    }

    public async Task RefreshAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default)
    {
        var accountIdentifierSet = accountIdentifiers
            .Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier))
            .ToHashSet(StringComparer.Ordinal);
        if (accountIdentifierSet.Count == 0) return;

        await SynchronizeActiveStatusesAsync(cancellationToken);
        await RefreshAccountStatesAsync(accountState => accountIdentifierSet.Contains(GetAccountIdentifier(accountState)), cancellationToken);
    }

    public async Task RefreshAccountsByActiveStateAsync(bool isActive, CancellationToken cancellationToken = default)
    {
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await RefreshAccountStatesAsync(accountState => GetIsActive(accountState) == isActive, cancellationToken);
    }

    public async Task RefreshActiveAccountAsync(CancellationToken cancellationToken = default)
    {
        await SynchronizeActiveStatusesAsync(cancellationToken);

        var activeAccountIdentifier = GetAccountStatesSnapshot().FirstOrDefault(GetIsActive) is { } activeAccountState ? GetAccountIdentifier(activeAccountState) : "";
        if (string.IsNullOrWhiteSpace(activeAccountIdentifier)) return;

        await RefreshAccountStatesAsync(accountState => string.Equals(GetAccountIdentifier(accountState), activeAccountIdentifier, StringComparison.Ordinal), cancellationToken);
    }

    public async Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var accountState = FindAccountState(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        var providerActivationFollowUp = await ActivateAccountCoreAsync(accountState, cancellationToken);
        await SynchronizeActiveStatusesAsync(cancellationToken);
        NotifyAccountsChanged();
        return providerActivationFollowUp;
    }

    public async Task DeleteAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var accountIdentifierSet = accountIdentifiers
            .Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier))
            .ToHashSet(StringComparer.Ordinal);
        if (accountIdentifierSet.Count == 0) return;

        var targetAccountStates = GetAccountStatesSnapshot()
            .Where(accountState => accountIdentifierSet.Contains(GetAccountIdentifier(accountState)))
            .ToArray();
        if (targetAccountStates.Length == 0) return;

        await DeleteAccountStatesCoreAsync(targetAccountStates, cancellationToken);
        RemoveAccountStates(accountIdentifierSet);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var expiredAccountStates = GetAccountStatesSnapshot().Where(GetIsTokenExpired).ToArray();
        if (expiredAccountStates.Length == 0) return 0;

        var accountIdentifierSet = expiredAccountStates.Select(GetAccountIdentifier).ToHashSet(StringComparer.Ordinal);
        await DeleteAccountStatesCoreAsync(expiredAccountStates, cancellationToken);
        RemoveAccountStates(accountIdentifierSet);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
        return expiredAccountStates.Length;
    }

    public virtual Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default) => throw new NotSupportedException("This provider does not support account rename.");

    public abstract Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    public abstract Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshSemaphore.Dispose();
    }

    protected abstract ProviderAccount CreateProviderAccount(TAccountState accountState);

    protected abstract string GetAccountIdentifier(TAccountState accountState);

    protected abstract string GetDisplayName(TAccountState accountState);

    protected abstract bool GetIsActive(TAccountState accountState);

    protected abstract void SetIsActive(TAccountState accountState, bool isActive);

    protected abstract bool GetIsTokenExpired(TAccountState accountState);

    protected abstract void MarkAccountAsExpired(TAccountState accountState);

    protected abstract ProviderUsageSnapshot GetProviderUsageSnapshot(TAccountState accountState);

    protected abstract Task<IReadOnlyList<TAccountState>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken);

    protected abstract Task SaveAccountStatesAsync(CancellationToken cancellationToken);

    protected abstract Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken);

    protected abstract Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(TAccountState accountState, CancellationToken cancellationToken);

    protected abstract Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(TAccountState accountState, CancellationToken cancellationToken);

    protected virtual Task DeleteAccountStatesCoreAsync(IReadOnlyList<TAccountState> accountStates, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual bool IsAccountExpiredException(Exception exception) => exception is WebException { Status: WebExceptionStatus.ProtocolError };

    protected IReadOnlyList<TAccountState> GetAccountStatesSnapshot()
    {
        lock (_accountsLock) return [.. _accounts];
    }

    protected TAccountState FindAccountState(string accountIdentifier)
    {
        lock (_accountsLock) return _accounts.FirstOrDefault(accountState => string.Equals(GetAccountIdentifier(accountState), accountIdentifier, StringComparison.Ordinal));
    }

    protected bool ContainsAccountState(string accountIdentifier)
    {
        lock (_accountsLock) return _accounts.Any(accountState => string.Equals(GetAccountIdentifier(accountState), accountIdentifier, StringComparison.Ordinal));
    }

    protected void UpsertAccountState(TAccountState accountState)
    {
        lock (_accountsLock)
        {
            var existingAccountStateIndex = _accounts.FindIndex(candidateAccountState => string.Equals(GetAccountIdentifier(candidateAccountState), GetAccountIdentifier(accountState), StringComparison.Ordinal));
            if (existingAccountStateIndex >= 0) _accounts[existingAccountStateIndex] = accountState;
            else _accounts.Add(accountState);
            SortAccountStates();
        }
    }

    protected void ReplaceAccountStates(IEnumerable<TAccountState> accountStates)
    {
        lock (_accountsLock)
        {
            _accounts.Clear();
            _accounts.AddRange(accountStates.Where(accountState => !string.IsNullOrWhiteSpace(GetAccountIdentifier(accountState))));
            SortAccountStates();
        }
    }

    protected void NotifyAccountsChanged() => AccountsChanged?.Invoke(this, EventArgs.Empty);

    protected static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);

    private async Task RefreshAccountStatesAsync(Func<TAccountState, bool> accountStatePredicate, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _refreshSemaphore.WaitAsync(cancellationToken);
        try
        {
            var targetAccountStates = GetAccountStatesSnapshot().Where(accountStatePredicate).ToList();
            foreach (var accountState in targetAccountStates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshAccountUsageAsync(accountState, cancellationToken);
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private async Task RefreshAccountUsageAsync(TAccountState accountState, CancellationToken cancellationToken)
    {
        try
        {
            var previousProviderUsageSnapshot = GetProviderUsageSnapshot(accountState);
            var wasPrimaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(previousProviderUsageSnapshot.FiveHour, ApplicationSettingsService.Settings.PrimaryUsageWarningThresholdPercentage);
            var wasSecondaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(previousProviderUsageSnapshot.SevenDay, ApplicationSettingsService.Settings.SecondaryUsageWarningThresholdPercentage);
            var currentProviderUsageSnapshot = await RefreshAccountUsageCoreAsync(accountState, cancellationToken);

            ShowLowQuotaNotifications(accountState, currentProviderUsageSnapshot, wasPrimaryUsageUnderWarningThreshold, wasSecondaryUsageUnderWarningThreshold);
            await SaveAccountStatesAsync(cancellationToken);
            NotifyAccountsChanged();
        }
        catch (Exception exception) when (IsAccountExpiredException(exception))
        {
            await HandleExpiredAccountAsync(accountState, cancellationToken);
        }
    }

    private async Task HandleExpiredAccountAsync(TAccountState accountState, CancellationToken cancellationToken)
    {
        var wasTokenExpired = GetIsTokenExpired(accountState);
        if (!wasTokenExpired && ApplicationSettingsService.Settings.IsExpiredAccountNotificationEnabled) ApplicationNotificationService.ShowExpiredAccountDetectedNotification(GetDisplayName(accountState));

        if (ApplicationSettingsService.Settings.IsExpiredAccountAutomaticDeletionEnabled)
        {
            await DeleteAccountStatesCoreAsync([accountState], cancellationToken);
            RemoveAccountState(GetAccountIdentifier(accountState));
            await SaveAccountStatesAsync(cancellationToken);
            NotifyAccountsChanged();
            return;
        }

        MarkAccountAsExpired(accountState);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    private void ShowLowQuotaNotifications(TAccountState accountState, ProviderUsageSnapshot currentProviderUsageSnapshot, bool wasPrimaryUsageUnderWarningThreshold, bool wasSecondaryUsageUnderWarningThreshold)
    {
        var applicationSettings = ApplicationSettingsService.Settings;
        var isPrimaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(currentProviderUsageSnapshot.FiveHour, applicationSettings.PrimaryUsageWarningThresholdPercentage);
        var isSecondaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(currentProviderUsageSnapshot.SevenDay, applicationSettings.SecondaryUsageWarningThresholdPercentage);
        var hasPrimaryUsageAlternativeAccountOverWarningThreshold = HasAlternativeAccountOverWarningThreshold(accountState, candidateAccountState => GetProviderUsageSnapshot(candidateAccountState).FiveHour, applicationSettings.PrimaryUsageWarningThresholdPercentage);
        var hasSecondaryUsageAlternativeAccountOverWarningThreshold = HasAlternativeAccountOverWarningThreshold(accountState, candidateAccountState => GetProviderUsageSnapshot(candidateAccountState).SevenDay, applicationSettings.SecondaryUsageWarningThresholdPercentage);
        var displayName = GetDisplayName(accountState);

        if (!wasPrimaryUsageUnderWarningThreshold && isPrimaryUsageUnderWarningThreshold && applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled) ApplicationNotificationService.ShowPrimaryUsageLowQuotaNotification(displayName, currentProviderUsageSnapshot.FiveHour.RemainingPercentage, hasPrimaryUsageAlternativeAccountOverWarningThreshold);
        if (!wasSecondaryUsageUnderWarningThreshold && isSecondaryUsageUnderWarningThreshold && applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled) ApplicationNotificationService.ShowSecondaryUsageLowQuotaNotification(displayName, currentProviderUsageSnapshot.SevenDay.RemainingPercentage, hasSecondaryUsageAlternativeAccountOverWarningThreshold);
    }

    private bool HasAlternativeAccountOverWarningThreshold(TAccountState sourceAccountState, Func<TAccountState, ProviderUsageWindow> usageWindowSelector, int usageWarningThresholdPercentage)
    {
        var accountStates = GetAccountStatesSnapshot();
        if (accountStates.Count < 2) return false;

        var normalizedUsageWarningThresholdPercentage = NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);
        var sourceAccountIdentifier = GetAccountIdentifier(sourceAccountState);
        return accountStates.Any(accountState => !string.Equals(GetAccountIdentifier(accountState), sourceAccountIdentifier, StringComparison.Ordinal) && usageWindowSelector(accountState).RemainingPercentage > normalizedUsageWarningThresholdPercentage);
    }

    private IReadOnlyList<TAccountState> SynchronizeActiveStatuses(string activeAccountIdentifier)
    {
        var changedAccountStates = new List<TAccountState>();

        lock (_accountsLock)
        {
            foreach (var accountState in _accounts)
            {
                var isActive = !string.IsNullOrWhiteSpace(activeAccountIdentifier) && string.Equals(GetAccountIdentifier(accountState), activeAccountIdentifier, StringComparison.Ordinal);
                if (GetIsActive(accountState) == isActive) continue;

                SetIsActive(accountState, isActive);
                changedAccountStates.Add(accountState);
            }

            if (changedAccountStates.Count > 0) SortAccountStates();
        }

        return changedAccountStates;
    }

    private void RemoveAccountStates(IReadOnlySet<string> accountIdentifierSet)
    {
        lock (_accountsLock) _accounts.RemoveAll(accountState => accountIdentifierSet.Contains(GetAccountIdentifier(accountState)));
    }

    private void RemoveAccountState(string accountIdentifier)
    {
        lock (_accountsLock) _accounts.RemoveAll(accountState => string.Equals(GetAccountIdentifier(accountState), accountIdentifier, StringComparison.Ordinal));
    }

    private void SortAccountStates() => _accounts.Sort((firstAccountState, secondAccountState) => CompareAccountStates(firstAccountState, secondAccountState));

    private int CompareAccountStates(TAccountState firstAccountState, TAccountState secondAccountState)
    {
        var activeComparison = GetIsActive(secondAccountState).CompareTo(GetIsActive(firstAccountState));
        return activeComparison != 0 ? activeComparison : StringComparer.CurrentCultureIgnoreCase.Compare(GetDisplayName(firstAccountState), GetDisplayName(secondAccountState));
    }

    private static bool IsUsageUnderWarningThreshold(ProviderUsageWindow providerUsageWindow, int usageWarningThresholdPercentage) => providerUsageWindow.RemainingPercentage >= 0 && providerUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
    }
}
