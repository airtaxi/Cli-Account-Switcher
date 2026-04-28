using CliAccountSwitcher.Api;
using CliAccountSwitcher.Api.Authentication;
using CliAccountSwitcher.Api.Infrastructure;
using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.Authentication;
using CliAccountSwitcher.Api.Models.OAuth;
using CliAccountSwitcher.Api.Models.Usage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class CodexAccountService : IDisposable
{
    private readonly object _accountsLock = new();
    private readonly List<CodexAccount> _accounts = [];
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly CancellationTokenSource _backgroundCancellationTokenSource = new();
    private readonly ApplicationSettingsService _applicationSettingsService;
    private readonly ApplicationNotificationService _applicationNotificationService;
    private readonly HttpClient _httpClient;
    private readonly CodexAuthenticationDocumentSerializer _codexAuthenticationDocumentSerializer = new();
    private readonly CodexOAuthClient _codexOAuthClient;
    private readonly CodexUsageClient _codexUsageClient;
    private readonly object _refreshScheduleLock = new();
    private FileSystemWatcher _authenticationFileSystemWatcher;
    private DateTimeOffset? _nextActiveUsageRefreshTime;
    private DateTimeOffset? _nextInactiveUsageRefreshTime;
    private bool _isUsageRefreshScheduleInitialized;
    private bool _disposed;

    public TimeSpan ActiveStatusRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ActiveUsageRefreshInterval { get; private set; } = TimeSpan.FromSeconds(ApplicationSettings.DefaultActiveAccountUsageRefreshIntervalSeconds);

    public TimeSpan InactiveUsageRefreshInterval { get; private set; } = TimeSpan.FromSeconds(ApplicationSettings.DefaultInactiveAccountUsageRefreshIntervalSeconds);

    public bool IsActiveUsageRefreshEnabled { get; private set; } = true;

    public bool IsInactiveUsageRefreshEnabled { get; private set; } = true;

    public CodexAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
    {
        _applicationSettingsService = applicationSettingsService;
        _applicationNotificationService = applicationNotificationService;

        var codexApiClientOptions = new CodexApiClientOptions
        {
            CodexHomeDirectoryPath = Constants.CodexHomeDirectory
        };
        var codexClientMetadataProvider = new CodexClientMetadataProvider(codexApiClientOptions);
        var codexRequestMessageFactory = new CodexRequestMessageFactory(codexApiClientOptions, codexClientMetadataProvider);

        _httpClient = CodexHttpClientFactory.CreateDefault();
        _codexOAuthClient = new CodexOAuthClient(_httpClient, codexApiClientOptions, codexRequestMessageFactory);
        _codexUsageClient = new CodexUsageClient(_httpClient, codexRequestMessageFactory);

        ApplyApplicationSettings();
        LoadAccounts();
    }

    public IReadOnlyList<CodexAccount> GetAccounts()
    {
        lock (_accountsLock) return [.. _accounts];
    }

    public TimeSpan? GetActiveUsageRefreshRemainingTime()
    {
        lock (_refreshScheduleLock) return GetUsageRefreshRemainingTime(_nextActiveUsageRefreshTime, IsActiveUsageRefreshEnabled);
    }

    public TimeSpan? GetInactiveUsageRefreshRemainingTime()
    {
        lock (_refreshScheduleLock) return GetUsageRefreshRemainingTime(_nextInactiveUsageRefreshTime, IsInactiveUsageRefreshEnabled);
    }

    public void Start()
    {
        ThrowIfDisposed();
        _applicationSettingsService.SettingsChanged += OnApplicationSettingsServiceSettingsChanged;
        StartAuthenticationFileSystemWatcher();
        _ = RunActiveStatusRefreshLoopAsync();
        _ = RunActiveUsageRefreshLoopAsync();
        _ = RunInactiveUsageRefreshLoopAsync();
        _ = RefreshActiveStatusesSilentlyAsync();
        if (IsActiveUsageRefreshEnabled) _ = RefreshAccountsUsageSilentlyAsync(account => account.IsActive);
        if (IsInactiveUsageRefreshEnabled) _ = RefreshAccountsUsageSilentlyAsync(account => !account.IsActive);
    }

    public CodexOAuthSession CreateOAuthSession() => _codexOAuthClient.CreateSession();

    public async Task<CodexAccount> AddOAuthCallbackAsync(CodexOAuthSession codexOAuthSession, CodexOAuthCallbackPayload codexOAuthCallbackPayload, CancellationToken cancellationToken = default)
    {
        var codexOAuthTokenExchangeResult = await _codexOAuthClient.ExchangeAuthorizationCodeAsync(codexOAuthSession, codexOAuthCallbackPayload, cancellationToken);
        var codexAuthenticationDocument = CodexOAuthClient.CreateAuthenticationDocument(codexOAuthTokenExchangeResult);
        return await AddValidatedAuthenticationDocumentAsync(codexAuthenticationDocument, "", cancellationToken);
    }

    public async Task<CodexAccount> AddValidatedAuthenticationDocumentAsync(CodexAuthenticationDocument codexAuthenticationDocument, string customAlias = "", CancellationToken cancellationToken = default)
    {
        var codexAccount = await CreateValidatedAccountAsync(codexAuthenticationDocument, customAlias, cancellationToken);
        UpsertAccount(codexAccount);
        await SaveAccountsAsync(cancellationToken);
        SendAccountChangedMessage(codexAccount);
        return codexAccount;
    }

    public async Task<CodexAccount> AddValidatedAuthenticationDocumentTextAsync(string authenticationDocumentText, string customAlias = "", CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
        return await AddValidatedAuthenticationDocumentAsync(codexAuthenticationDocument, customAlias, cancellationToken);
    }

    public async Task<CodexAccount> AddCurrentAuthenticationDocumentAsync(CancellationToken cancellationToken = default)
    {
        var authenticationDocumentText = await File.ReadAllTextAsync(Constants.CurrentAuthenticationFilePath, cancellationToken);
        return await AddValidatedAuthenticationDocumentTextAsync(authenticationDocumentText, "", cancellationToken);
    }

    public async Task RefreshAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        await RefreshActiveStatusesAsync(cancellationToken);
        await RefreshAccountsUsageAsync(_ => true, cancellationToken);
    }

    public async Task RefreshAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default)
    {
        var accountIdentifierSet = accountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier)).ToHashSet(StringComparer.Ordinal);
        if (accountIdentifierSet.Count == 0) return;

        await RefreshActiveStatusesAsync(cancellationToken);
        await RefreshAccountsUsageAsync(account => accountIdentifierSet.Contains(account.AccountIdentifier), cancellationToken);
    }

    public async Task RefreshActiveAccountAsync(CancellationToken cancellationToken = default)
    {
        await RefreshActiveStatusesAsync(cancellationToken);

        var activeAccountIdentifier = GetAccounts().FirstOrDefault(account => account.IsActive)?.AccountIdentifier;
        if (string.IsNullOrWhiteSpace(activeAccountIdentifier)) return;

        await RefreshAccountsUsageAsync(account => string.Equals(account.AccountIdentifier, activeAccountIdentifier, StringComparison.Ordinal), cancellationToken);
        ResetActiveUsageRefreshSchedule();
    }

    public async Task SwitchActiveAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default)
    {
        var codexAccount = FindAccount(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        var authenticationDocumentText = _codexAuthenticationDocumentSerializer.Serialize(codexAccount.CodexAuthenticationDocument);
        Directory.CreateDirectory(Constants.CodexHomeDirectory);
        await File.WriteAllTextAsync(Constants.CurrentAuthenticationFilePath, authenticationDocumentText, cancellationToken);
        await RefreshActiveStatusesAsync(cancellationToken);
    }

    public async Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default)
    {
        var codexAccount = FindAccount(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        codexAccount.CustomAlias = customAlias.Trim();
        await SaveAccountsAsync(cancellationToken);
        SendAccountChangedMessage(codexAccount);
    }

    public async Task DeleteAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default)
    {
        var accountIdentifierSet = accountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier)).ToHashSet(StringComparer.Ordinal);
        if (accountIdentifierSet.Count == 0) return;

        lock (_accountsLock) _accounts.RemoveAll(account => accountIdentifierSet.Contains(account.AccountIdentifier));
        await SaveAccountsAsync(cancellationToken);
        SendAccountsChangedMessage();
    }

    public async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default)
    {
        int deletedCount;
        lock (_accountsLock) deletedCount = _accounts.RemoveAll(account => account.IsTokenExpired);
        if (deletedCount > 0) await SaveAccountsAsync(cancellationToken);
        if (deletedCount > 0) SendAccountsChangedMessage();
        return deletedCount;
    }

    public async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
        using var zipArchive = ZipFile.Open(backupFilePath, ZipArchiveMode.Create);
        var zipArchiveEntry = zipArchive.CreateEntry("accounts.json", CompressionLevel.Optimal);
        await using var zipArchiveEntryStream = zipArchiveEntry.Open();
        var codexAccountStoreDocument = CreateStoreDocumentSnapshot();
        await JsonSerializer.SerializeAsync(zipArchiveEntryStream, codexAccountStoreDocument, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument, cancellationToken);
    }

    public async Task<CodexAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var codexAccountBackupImportResult = new CodexAccountBackupImportResult();
        var importedAccountIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        using var zipArchive = ZipFile.OpenRead(backupFilePath);
        foreach (var zipArchiveEntry in zipArchive.Entries.Where(zipArchiveEntry => string.Equals(Path.GetExtension(zipArchiveEntry.FullName), ".json", StringComparison.OrdinalIgnoreCase)))
        {
            var candidateAccounts = await ReadBackupEntryAccountsAsync(zipArchiveEntry, cancellationToken);
            if (candidateAccounts.Count == 0)
            {
                codexAccountBackupImportResult.FailureCount++;
                continue;
            }

            foreach (var candidateAccount in candidateAccounts)
            {
                var accountIdentifier = TryGetAccountIdentifier(candidateAccount.CodexAuthenticationDocument);
                if (string.IsNullOrWhiteSpace(accountIdentifier))
                {
                    codexAccountBackupImportResult.FailureCount++;
                    continue;
                }

                if (ContainsAccount(accountIdentifier) || importedAccountIdentifiers.Contains(accountIdentifier))
                {
                    codexAccountBackupImportResult.DuplicateCount++;
                    continue;
                }

                try
                {
                    var validatedAccount = await CreateValidatedAccountAsync(candidateAccount.CodexAuthenticationDocument, candidateAccount.CustomAlias, cancellationToken);
                    UpsertAccount(validatedAccount);
                    importedAccountIdentifiers.Add(validatedAccount.AccountIdentifier);
                    codexAccountBackupImportResult.SuccessCount++;
                    SendAccountChangedMessage(validatedAccount);
                }
                catch { codexAccountBackupImportResult.FailureCount++; }
            }
        }

        if (codexAccountBackupImportResult.SuccessCount > 0) await SaveAccountsAsync(cancellationToken);
        return codexAccountBackupImportResult;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backgroundCancellationTokenSource.Cancel();
        _applicationSettingsService.SettingsChanged -= OnApplicationSettingsServiceSettingsChanged;
        _authenticationFileSystemWatcher?.Dispose();
        _backgroundCancellationTokenSource.Dispose();
        _saveSemaphore.Dispose();
        _refreshSemaphore.Dispose();
        _httpClient.Dispose();
    }

    private async Task<CodexAccount> CreateValidatedAccountAsync(CodexAuthenticationDocument codexAuthenticationDocument, string customAlias, CancellationToken cancellationToken)
    {
        var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(codexAuthenticationDocument, cancellationToken);
        var codexAccount = new CodexAccount
        {
            CodexAuthenticationDocument = codexAuthenticationDocument,
            CustomAlias = customAlias.Trim(),
            LastCodexUsageSnapshot = codexUsageSnapshot,
            LastUsageRefreshTime = DateTimeOffset.UtcNow
        };
        codexAccount.MarkAsValid();
        UpdateEmailAddress(codexAccount, codexUsageSnapshot);
        return codexAccount;
    }

    private async Task RefreshAccountsUsageAsync(Func<CodexAccount, bool> accountPredicate, CancellationToken cancellationToken)
    {
        await _refreshSemaphore.WaitAsync(cancellationToken);
        try
        {
            var targetAccounts = GetAccounts().Where(accountPredicate).ToList();
            foreach (var codexAccount in targetAccounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshAccountUsageAsync(codexAccount.AccountIdentifier, cancellationToken);
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private async Task RefreshAccountUsageAsync(string accountIdentifier, CancellationToken cancellationToken)
    {
        var codexAccount = FindAccount(accountIdentifier);
        if (codexAccount is null) return;

        try
        {
            var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(codexAccount.CodexAuthenticationDocument, cancellationToken);
            var wasPrimaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(codexAccount.LastCodexUsageSnapshot.PrimaryWindow, _applicationSettingsService.Settings.PrimaryUsageWarningThresholdPercentage);
            var wasSecondaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(codexAccount.LastCodexUsageSnapshot.SecondaryWindow, _applicationSettingsService.Settings.SecondaryUsageWarningThresholdPercentage);

            codexAccount.LastCodexUsageSnapshot = codexUsageSnapshot;
            codexAccount.LastUsageRefreshTime = DateTimeOffset.UtcNow;
            codexAccount.MarkAsValid();
            UpdateEmailAddress(codexAccount, codexUsageSnapshot);
            ShowLowQuotaNotifications(codexAccount, wasPrimaryUsageUnderWarningThreshold, wasSecondaryUsageUnderWarningThreshold);
            await SaveAccountsAsync(cancellationToken);
            SendAccountChangedMessage(codexAccount);
        }
        catch (CodexApiException codexApiException) when (codexApiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await HandleExpiredAccountAsync(codexAccount, cancellationToken);
        }
    }

    private async Task RefreshActiveStatusesAsync(CancellationToken cancellationToken)
    {
        var activeAccountIdentifier = TryReadActiveAccountIdentifier();
        var changedAccounts = new List<CodexAccount>();

        lock (_accountsLock)
        {
            foreach (var codexAccount in _accounts)
            {
                var isActive = !string.IsNullOrWhiteSpace(activeAccountIdentifier) && string.Equals(codexAccount.AccountIdentifier, activeAccountIdentifier, StringComparison.Ordinal);
                if (codexAccount.IsActive == isActive) continue;

                codexAccount.IsActive = isActive;
                changedAccounts.Add(codexAccount);
            }
        }

        if (changedAccounts.Count == 0) return;
        await SaveAccountsAsync(cancellationToken);
        foreach (var codexAccount in changedAccounts) SendAccountChangedMessage(codexAccount);
    }

    private async Task<IReadOnlyList<CodexAccount>> ReadBackupEntryAccountsAsync(ZipArchiveEntry zipArchiveEntry, CancellationToken cancellationToken)
    {
        await using var zipArchiveEntryStream = zipArchiveEntry.Open();
        using var streamReader = new StreamReader(zipArchiveEntryStream);
        var authenticationDocumentText = await streamReader.ReadToEndAsync(cancellationToken);
        return ReadAccountsFromJsonText(authenticationDocumentText);
    }

    private IReadOnlyList<CodexAccount> ReadAccountsFromJsonText(string authenticationDocumentText)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(authenticationDocumentText);
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array) return JsonSerializer.Deserialize(authenticationDocumentText, CodexAccountJsonSerializerContext.Default.ListCodexAccount) ?? [];
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object) return [];

            if (jsonDocument.RootElement.TryGetProperty(nameof(CodexAccountStoreDocument.Accounts), out _))
            {
                var codexAccountStoreDocument = JsonSerializer.Deserialize(authenticationDocumentText, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument);
                return codexAccountStoreDocument?.Accounts ?? [];
            }

            if (jsonDocument.RootElement.TryGetProperty(nameof(CodexAccount.CodexAuthenticationDocument), out _))
            {
                var codexAccount = JsonSerializer.Deserialize(authenticationDocumentText, CodexAccountJsonSerializerContext.Default.CodexAccount);
                return codexAccount is null ? [] : [codexAccount];
            }

            var codexAuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
            return [new CodexAccount { CodexAuthenticationDocument = codexAuthenticationDocument }];
        }
        catch { return []; }
    }

    private async Task RunActiveStatusRefreshLoopAsync()
    {
        using var periodicTimer = new PeriodicTimer(ActiveStatusRefreshInterval);
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(_backgroundCancellationTokenSource.Token)) await RefreshActiveStatusesSilentlyAsync();
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
                await RefreshAccountsUsageSilentlyAsync(account => account.IsActive);
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
                await RefreshAccountsUsageSilentlyAsync(account => !account.IsActive);
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

    private async Task HandleExpiredAccountAsync(CodexAccount codexAccount, CancellationToken cancellationToken)
    {
        var wasTokenExpired = codexAccount.IsTokenExpired;
        if (!wasTokenExpired && _applicationSettingsService.Settings.IsExpiredAccountNotificationEnabled) _applicationNotificationService.ShowExpiredAccountDetectedNotification(codexAccount.DisplayName);

        if (_applicationSettingsService.Settings.IsExpiredAccountAutomaticDeletionEnabled)
        {
            lock (_accountsLock) _accounts.RemoveAll(account => string.Equals(account.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
            await SaveAccountsAsync(cancellationToken);
            SendAccountsChangedMessage();
            return;
        }

        codexAccount.MarkAsExpired();
        await SaveAccountsAsync(cancellationToken);
        SendAccountChangedMessage(codexAccount);
    }

    private void ShowLowQuotaNotifications(CodexAccount codexAccount, bool wasPrimaryUsageUnderWarningThreshold, bool wasSecondaryUsageUnderWarningThreshold)
    {
        var applicationSettings = _applicationSettingsService.Settings;
        var isPrimaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(codexAccount.LastCodexUsageSnapshot.PrimaryWindow, applicationSettings.PrimaryUsageWarningThresholdPercentage);
        var isSecondaryUsageUnderWarningThreshold = IsUsageUnderWarningThreshold(codexAccount.LastCodexUsageSnapshot.SecondaryWindow, applicationSettings.SecondaryUsageWarningThresholdPercentage);
        var hasPrimaryUsageAlternativeAccountOverWarningThreshold = HasAlternativeAccountOverWarningThreshold(codexAccount, alternativeCodexAccount => alternativeCodexAccount.LastCodexUsageSnapshot.PrimaryWindow, applicationSettings.PrimaryUsageWarningThresholdPercentage);
        var hasSecondaryUsageAlternativeAccountOverWarningThreshold = HasAlternativeAccountOverWarningThreshold(codexAccount, alternativeCodexAccount => alternativeCodexAccount.LastCodexUsageSnapshot.SecondaryWindow, applicationSettings.SecondaryUsageWarningThresholdPercentage);

        if (!wasPrimaryUsageUnderWarningThreshold && isPrimaryUsageUnderWarningThreshold && applicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled) _applicationNotificationService.ShowPrimaryUsageLowQuotaNotification(codexAccount.DisplayName, codexAccount.LastCodexUsageSnapshot.PrimaryWindow.RemainingPercentage, hasPrimaryUsageAlternativeAccountOverWarningThreshold);
        if (!wasSecondaryUsageUnderWarningThreshold && isSecondaryUsageUnderWarningThreshold && applicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled) _applicationNotificationService.ShowSecondaryUsageLowQuotaNotification(codexAccount.DisplayName, codexAccount.LastCodexUsageSnapshot.SecondaryWindow.RemainingPercentage, hasSecondaryUsageAlternativeAccountOverWarningThreshold);
    }

    private bool HasAlternativeAccountOverWarningThreshold(CodexAccount sourceCodexAccount, Func<CodexAccount, CodexUsageWindow> codexUsageWindowSelector, int usageWarningThresholdPercentage)
    {
        var codexAccounts = GetAccounts();
        if (codexAccounts.Count < 2) return false;

        var normalizedUsageWarningThresholdPercentage = NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);
        return codexAccounts.Any(codexAccount => !string.Equals(codexAccount.AccountIdentifier, sourceCodexAccount.AccountIdentifier, StringComparison.Ordinal) && codexUsageWindowSelector(codexAccount).RemainingPercentage > normalizedUsageWarningThresholdPercentage);
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

    private async Task RefreshActiveStatusesSilentlyAsync()
    {
        try { await RefreshActiveStatusesAsync(_backgroundCancellationTokenSource.Token); }
        catch { }
    }

    private async Task RefreshAccountsUsageSilentlyAsync(Func<CodexAccount, bool> accountPredicate)
    {
        try { await RefreshAccountsUsageAsync(accountPredicate, _backgroundCancellationTokenSource.Token); }
        catch { }
    }

    private void StartAuthenticationFileSystemWatcher()
    {
        try
        {
            Directory.CreateDirectory(Constants.CodexHomeDirectory);
            _authenticationFileSystemWatcher = new FileSystemWatcher(Constants.CodexHomeDirectory, "auth.json")
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _authenticationFileSystemWatcher.Changed += OnAuthenticationFileSystemWatcherChanged;
            _authenticationFileSystemWatcher.Created += OnAuthenticationFileSystemWatcherChanged;
            _authenticationFileSystemWatcher.Renamed += OnAuthenticationFileSystemWatcherRenamed;
            _authenticationFileSystemWatcher.Deleted += OnAuthenticationFileSystemWatcherChanged;
            _authenticationFileSystemWatcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void OnAuthenticationFileSystemWatcherChanged(object sender, FileSystemEventArgs fileSystemEventArguments) => _ = RefreshActiveStatusesSilentlyAsync();

    private void OnAuthenticationFileSystemWatcherRenamed(object sender, RenamedEventArgs renamedEventArguments) => _ = RefreshActiveStatusesSilentlyAsync();

    private void LoadAccounts()
    {
        try
        {
            if (!File.Exists(Constants.AccountsFilePath)) return;

            using var fileStream = File.OpenRead(Constants.AccountsFilePath);
            var codexAccountStoreDocument = JsonSerializer.Deserialize(fileStream, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument);
            if (codexAccountStoreDocument?.Accounts is null) return;

            lock (_accountsLock)
            {
                _accounts.Clear();
                _accounts.AddRange(codexAccountStoreDocument.Accounts.Where(account => !string.IsNullOrWhiteSpace(TryGetAccountIdentifier(account.CodexAuthenticationDocument))));
            }
        }
        catch { }
    }

    private async Task SaveAccountsAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Constants.UserDataDirectory);
            var codexAccountStoreDocument = CreateStoreDocumentSnapshot();
            await using var fileStream = File.Create(Constants.AccountsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, codexAccountStoreDocument, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument, cancellationToken);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private CodexAccountStoreDocument CreateStoreDocumentSnapshot()
    {
        lock (_accountsLock)
        {
            return new CodexAccountStoreDocument
            {
                Accounts = [.. _accounts]
            };
        }
    }

    private void UpsertAccount(CodexAccount codexAccount)
    {
        lock (_accountsLock)
        {
            var existingAccountIndex = _accounts.FindIndex(account => string.Equals(account.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
            if (existingAccountIndex >= 0) _accounts[existingAccountIndex] = codexAccount;
            else _accounts.Add(codexAccount);
        }
    }

    private CodexAccount FindAccount(string accountIdentifier)
    {
        lock (_accountsLock) return _accounts.FirstOrDefault(account => string.Equals(account.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
    }

    private bool ContainsAccount(string accountIdentifier)
    {
        lock (_accountsLock) return _accounts.Any(account => string.Equals(account.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
    }

    private string TryReadActiveAccountIdentifier()
    {
        try
        {
            if (!File.Exists(Constants.CurrentAuthenticationFilePath)) return "";
            var authenticationDocumentText = File.ReadAllText(Constants.CurrentAuthenticationFilePath);
            var codexAuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
            return codexAuthenticationDocument.GetEffectiveAccountIdentifier();
        }
        catch { return ""; }
    }

    private static string TryGetAccountIdentifier(CodexAuthenticationDocument codexAuthenticationDocument)
    {
        try { return codexAuthenticationDocument.GetEffectiveAccountIdentifier(); }
        catch { return ""; }
    }

    private static void UpdateEmailAddress(CodexAccount codexAccount, CodexUsageSnapshot codexUsageSnapshot)
    {
        if (!string.IsNullOrWhiteSpace(codexUsageSnapshot.EmailAddress)) codexAccount.CodexAuthenticationDocument.EmailAddress = codexUsageSnapshot.EmailAddress;
    }

    private static bool IsUsageUnderWarningThreshold(CodexUsageWindow codexUsageWindow, int usageWarningThresholdPercentage) => codexUsageWindow.RemainingPercentage >= 0 && codexUsageWindow.RemainingPercentage <= NormalizeUsageWarningThresholdPercentage(usageWarningThresholdPercentage);

    private static int NormalizeUsageWarningThresholdPercentage(int usageWarningThresholdPercentage) => Math.Clamp(usageWarningThresholdPercentage, 0, 100);

    private static void SendAccountChangedMessage(CodexAccount codexAccount) => WeakReferenceMessenger.Default.Send(new ValueChangedMessage<CodexAccount>(codexAccount));

    private void SendAccountsChangedMessage() => WeakReferenceMessenger.Default.Send(new ValueChangedMessage<CodexAccountStoreDocument>(CreateStoreDocumentSnapshot()));

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CodexAccountService));
    }
}
