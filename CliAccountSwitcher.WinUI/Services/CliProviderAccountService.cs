using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Claude;
using CliAccountSwitcher.Api.Security;
using CliAccountSwitcher.Api.Storage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class CliProviderAccountService : IDisposable
{
    private readonly object _claudeCodeUsageCacheLock = new();
    private readonly FileSystemProviderSnapshotStore _providerSnapshotStore;
    private readonly ClaudeCodeProviderAdapter _claudeCodeProviderAdapter;
    private readonly Dictionary<string, ProviderUsageSnapshot> _claudeCodeUsageSnapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _claudeCodeUsageRefreshTimes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _claudeCodeRefreshSemaphore = new(1, 1);

    public CliProviderAccountService()
    {
        _providerSnapshotStore = new FileSystemProviderSnapshotStore(Constants.ProviderSnapshotsDirectory, new WindowsDataProtectionService());
        _claudeCodeProviderAdapter = new ClaudeCodeProviderAdapter(_providerSnapshotStore);
    }

    public async Task<IReadOnlyList<StoredProviderAccount>> GetClaudeCodeAccountsAsync(CancellationToken cancellationToken = default) => await _claudeCodeProviderAdapter.ListStoredAccountsAsync(_providerSnapshotStore, cancellationToken);

    public async Task<StoredProviderAccount> SaveCurrentClaudeCodeAccountAsync(CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveCurrentAccountAsync(_providerSnapshotStore, cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveCurrentClaudeCodeAccountWithoutActivationAsync(CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveCurrentAccountWithoutActivationAsync(_providerSnapshotStore, cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveClaudeCodeAccountAsync(string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveAccountAsync(_providerSnapshotStore, credentialsJson, globalConfigJson, cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveClaudeCodeAccountWithoutActivationAsync(string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveAccountWithoutActivationAsync(_providerSnapshotStore, credentialsJson, globalConfigJson, cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> ActivateClaudeCodeAccountAsync(string storedAccountIdentifier, CancellationToken cancellationToken = default) => await _claudeCodeProviderAdapter.ActivateStoredAccountAsync(_providerSnapshotStore, storedAccountIdentifier, cancellationToken);

    public async Task DeleteClaudeCodeAccountsAsync(IEnumerable<string> storedAccountIdentifiers, CancellationToken cancellationToken = default)
    {
        foreach (var storedAccountIdentifier in storedAccountIdentifiers.Where(storedAccountIdentifier => !string.IsNullOrWhiteSpace(storedAccountIdentifier)).Distinct(StringComparer.Ordinal))
        {
            await _claudeCodeProviderAdapter.DeleteStoredAccountAsync(_providerSnapshotStore, storedAccountIdentifier, cancellationToken);
            ClearClaudeCodeUsageSnapshot(storedAccountIdentifier);
        }
    }

    public async Task RunClaudeCodeLoginAsync(CancellationToken cancellationToken = default) => await _claudeCodeProviderAdapter.RunLoginAsync(cancellationToken);

    public async Task<ProviderUsageSnapshot> GetClaudeCodeUsageAsync(string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var providerUsageSnapshot = await _claudeCodeProviderAdapter.GetUsageAsync(storedAccountIdentifier, cancellationToken);
            var usageRefreshTime = DateTimeOffset.UtcNow;
            SetClaudeCodeUsageSnapshot(storedAccountIdentifier, providerUsageSnapshot, usageRefreshTime);
            await UpdateClaudeCodeStoredAccountMetadataAsync(storedAccountIdentifier, false, usageRefreshTime, cancellationToken);
            return providerUsageSnapshot;
        }
        catch (ProviderActionRequiredException)
        {
            ClearClaudeCodeUsageSnapshot(storedAccountIdentifier);
            await UpdateClaudeCodeStoredAccountMetadataAsync(storedAccountIdentifier, true, DateTimeOffset.UtcNow, cancellationToken);
            throw;
        }
    }

    public async Task RefreshClaudeCodeAccountsAsync(IEnumerable<string> storedAccountIdentifiers, CancellationToken cancellationToken = default)
    {
        var storedAccountIdentifierSet = storedAccountIdentifiers
            .Where(storedAccountIdentifier => !string.IsNullOrWhiteSpace(storedAccountIdentifier))
            .ToHashSet(StringComparer.Ordinal);
        if (storedAccountIdentifierSet.Count == 0) return;

        await _claudeCodeRefreshSemaphore.WaitAsync(cancellationToken);
        try
        {
            var storedProviderAccounts = await GetClaudeCodeAccountsAsync(cancellationToken);
            foreach (var storedProviderAccount in storedProviderAccounts.Where(storedProviderAccount => storedAccountIdentifierSet.Contains(storedProviderAccount.StoredAccountIdentifier)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshClaudeCodeAccountAsync(storedProviderAccount, cancellationToken);
            }
        }
        finally
        {
            _claudeCodeRefreshSemaphore.Release();
        }
    }

    public async Task RefreshAllClaudeCodeAccountsAsync(CancellationToken cancellationToken = default)
    {
        var storedProviderAccounts = await GetClaudeCodeAccountsAsync(cancellationToken);
        await RefreshClaudeCodeAccountsAsync(storedProviderAccounts.Select(storedProviderAccount => storedProviderAccount.StoredAccountIdentifier), cancellationToken);
    }

    public async Task RefreshActiveClaudeCodeAccountAsync(CancellationToken cancellationToken = default)
    {
        var activeStoredAccountIdentifier = (await GetClaudeCodeAccountsAsync(cancellationToken)).FirstOrDefault(storedProviderAccount => storedProviderAccount.IsActive)?.StoredAccountIdentifier;
        if (string.IsNullOrWhiteSpace(activeStoredAccountIdentifier)) return;

        await RefreshClaudeCodeAccountsAsync([activeStoredAccountIdentifier], cancellationToken);
    }

    public async Task<int> DeleteExpiredClaudeCodeAccountsAsync(CancellationToken cancellationToken = default)
    {
        var expiredStoredAccountIdentifiers = (await GetClaudeCodeAccountsAsync(cancellationToken))
            .Where(storedProviderAccount => storedProviderAccount.IsTokenExpired)
            .Select(storedProviderAccount => storedProviderAccount.StoredAccountIdentifier)
            .ToArray();
        if (expiredStoredAccountIdentifiers.Length == 0) return 0;

        await DeleteClaudeCodeAccountsAsync(expiredStoredAccountIdentifiers, cancellationToken);
        return expiredStoredAccountIdentifiers.Length;
    }

    public async Task ExportClaudeCodeBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var backupDocument = new ClaudeCodeBackupDocument();
        var storedProviderAccounts = await GetClaudeCodeAccountsAsync(cancellationToken);

        foreach (var storedProviderAccount in storedProviderAccounts)
        {
            var payloadJson = await _providerSnapshotStore.GetPayloadJsonAsync(CliProviderKind.ClaudeCode, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
            if (string.IsNullOrWhiteSpace(payloadJson)) throw new InvalidDataException($"The stored Claude Code account payload was not found: {storedProviderAccount.StoredAccountIdentifier}");

            var backupAccountDocument = CreateClaudeCodeBackupAccountDocument(payloadJson);
            _ = ClaudeCodeCredentialDocument.Parse(backupAccountDocument.CredentialsJson);
            _ = ClaudeCodeGlobalConfigDocument.Parse(backupAccountDocument.GlobalConfigJson);
            backupDocument.Accounts.Add(backupAccountDocument);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        await using var fileStream = File.Create(backupFilePath);
        await JsonSerializer.SerializeAsync(fileStream, backupDocument, CodexAccountJsonSerializerContext.Default.ClaudeCodeBackupDocument, cancellationToken);
    }

    public async Task<ProviderAccountBackupImportResult> ImportClaudeCodeBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        await using var fileStream = File.OpenRead(backupFilePath);
        var backupDocument = await JsonSerializer.DeserializeAsync(fileStream, CodexAccountJsonSerializerContext.Default.ClaudeCodeBackupDocument, cancellationToken);
        if (backupDocument is null) throw new InvalidDataException("The Claude Code backup document is empty.");
        if (backupDocument.SchemaVersion != 1) throw new InvalidDataException($"Unsupported Claude Code backup schema version: {backupDocument.SchemaVersion}");
        if (!string.Equals(backupDocument.ProviderKind, CliProviderKind.ClaudeCode.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The backup document is not for Claude Code.");

        var importResult = new ProviderAccountBackupImportResult();
        var duplicateKeys = (await GetClaudeCodeAccountsAsync(cancellationToken))
            .Select(CreateClaudeCodeDuplicateKey)
            .Where(duplicateKey => !string.IsNullOrWhiteSpace(duplicateKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var backupAccountDocument in backupDocument.Accounts ?? [])
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupAccountDocument.CredentialsJson) || string.IsNullOrWhiteSpace(backupAccountDocument.GlobalConfigJson))
                {
                    importResult.FailureCount++;
                    continue;
                }

                _ = ClaudeCodeCredentialDocument.Parse(backupAccountDocument.CredentialsJson);
                var globalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(backupAccountDocument.GlobalConfigJson);
                var duplicateKey = CreateClaudeCodeDuplicateKey(globalConfigDocument.EmailAddress, globalConfigDocument.OrganizationIdentifier);
                if (string.IsNullOrWhiteSpace(duplicateKey))
                {
                    importResult.FailureCount++;
                    continue;
                }

                if (duplicateKeys.Contains(duplicateKey))
                {
                    importResult.DuplicateCount++;
                    continue;
                }

                await SaveClaudeCodeAccountWithoutActivationAsync(backupAccountDocument.CredentialsJson, backupAccountDocument.GlobalConfigJson, cancellationToken);
                duplicateKeys.Add(duplicateKey);
                importResult.SuccessCount++;
            }
            catch
            {
                importResult.FailureCount++;
            }
        }

        return importResult;
    }

    public bool TryGetClaudeCodeUsageSnapshot(string storedAccountIdentifier, out ProviderUsageSnapshot providerUsageSnapshot)
    {
        lock (_claudeCodeUsageCacheLock) return _claudeCodeUsageSnapshots.TryGetValue(storedAccountIdentifier, out providerUsageSnapshot);
    }

    public bool TryGetClaudeCodeUsageRefreshTime(string storedAccountIdentifier, out DateTimeOffset usageRefreshTime)
    {
        lock (_claudeCodeUsageCacheLock) return _claudeCodeUsageRefreshTimes.TryGetValue(storedAccountIdentifier, out usageRefreshTime);
    }

    public void ClearClaudeCodeUsageSnapshot(string storedAccountIdentifier)
    {
        lock (_claudeCodeUsageCacheLock)
        {
            _claudeCodeUsageSnapshots.Remove(storedAccountIdentifier);
            _claudeCodeUsageRefreshTimes.Remove(storedAccountIdentifier);
        }
    }

    public void Dispose()
    {
        _claudeCodeRefreshSemaphore.Dispose();
        _claudeCodeProviderAdapter.Dispose();
    }

    private async Task<StoredProviderAccount> SaveAndRefreshClaudeCodeAccountAsync(Task<StoredProviderAccount> saveAccountTask, CancellationToken cancellationToken)
    {
        var storedProviderAccount = await saveAccountTask;
        await RefreshClaudeCodeAccountAsync(storedProviderAccount, cancellationToken);
        return storedProviderAccount;
    }

    private async Task RefreshClaudeCodeAccountAsync(StoredProviderAccount storedProviderAccount, CancellationToken cancellationToken)
    {
        try
        {
            var providerUsageSnapshot = await _claudeCodeProviderAdapter.GetUsageAsync(storedProviderAccount.StoredAccountIdentifier, cancellationToken);
            var usageRefreshTime = DateTimeOffset.UtcNow;
            SetClaudeCodeUsageSnapshot(storedProviderAccount.StoredAccountIdentifier, providerUsageSnapshot, usageRefreshTime);
            await UpdateClaudeCodeStoredAccountMetadataAsync(storedProviderAccount, false, usageRefreshTime, cancellationToken);
        }
        catch (ProviderActionRequiredException)
        {
            ClearClaudeCodeUsageSnapshot(storedProviderAccount.StoredAccountIdentifier);
            await UpdateClaudeCodeStoredAccountMetadataAsync(storedProviderAccount, true, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { }
    }

    private void SetClaudeCodeUsageSnapshot(string storedAccountIdentifier, ProviderUsageSnapshot providerUsageSnapshot, DateTimeOffset usageRefreshTime)
    {
        lock (_claudeCodeUsageCacheLock)
        {
            _claudeCodeUsageSnapshots[storedAccountIdentifier] = providerUsageSnapshot;
            _claudeCodeUsageRefreshTimes[storedAccountIdentifier] = usageRefreshTime;
        }
    }

    private async Task UpdateClaudeCodeStoredAccountMetadataAsync(string storedAccountIdentifier, bool isTokenExpired, DateTimeOffset lastUpdated, CancellationToken cancellationToken)
    {
        var storedProviderAccount = (await GetClaudeCodeAccountsAsync(cancellationToken)).FirstOrDefault(storedProviderAccount => string.Equals(storedProviderAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.Ordinal));
        if (storedProviderAccount is null) return;

        await UpdateClaudeCodeStoredAccountMetadataAsync(storedProviderAccount, isTokenExpired, lastUpdated, cancellationToken);
    }

    private async Task UpdateClaudeCodeStoredAccountMetadataAsync(StoredProviderAccount storedProviderAccount, bool isTokenExpired, DateTimeOffset lastUpdated, CancellationToken cancellationToken)
    {
        var payloadJson = await _providerSnapshotStore.GetPayloadJsonAsync(CliProviderKind.ClaudeCode, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) return;

        var updatedStoredProviderAccount = CloneStoredProviderAccount(storedProviderAccount);
        updatedStoredProviderAccount.IsTokenExpired = isTokenExpired;
        updatedStoredProviderAccount.LastUpdated = lastUpdated;
        await _providerSnapshotStore.SaveAsync(updatedStoredProviderAccount, payloadJson, cancellationToken);
    }

    private static ClaudeCodeBackupAccountDocument CreateClaudeCodeBackupAccountDocument(string payloadJson)
    {
        using var jsonDocument = JsonDocument.Parse(payloadJson);
        var credentialsJson = ReadJsonString(jsonDocument.RootElement, "credentialsJson");
        var globalConfigJson = ReadJsonString(jsonDocument.RootElement, "globalConfigJson");
        if (string.IsNullOrWhiteSpace(credentialsJson) || string.IsNullOrWhiteSpace(globalConfigJson)) throw new InvalidDataException("The stored Claude Code account payload is invalid.");

        return new ClaudeCodeBackupAccountDocument
        {
            CredentialsJson = credentialsJson,
            GlobalConfigJson = globalConfigJson
        };
    }

    private static string CreateClaudeCodeDuplicateKey(StoredProviderAccount storedProviderAccount) => CreateClaudeCodeDuplicateKey(storedProviderAccount.EmailAddress, storedProviderAccount.OrganizationIdentifier);

    private static string CreateClaudeCodeDuplicateKey(string emailAddress, string organizationIdentifier)
    {
        if (string.IsNullOrWhiteSpace(emailAddress) || string.IsNullOrWhiteSpace(organizationIdentifier)) return "";
        return $"{emailAddress.Trim()}|{organizationIdentifier.Trim()}";
    }

    private static string ReadJsonString(JsonElement jsonElement, string propertyName)
    {
        foreach (var jsonProperty in jsonElement.EnumerateObject())
        {
            if (!string.Equals(jsonProperty.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            return jsonProperty.Value.ValueKind == JsonValueKind.String ? jsonProperty.Value.GetString() ?? "" : jsonProperty.Value.ToString();
        }

        return "";
    }

    private static StoredProviderAccount CloneStoredProviderAccount(StoredProviderAccount storedProviderAccount)
        => new()
        {
            ProviderKind = storedProviderAccount.ProviderKind,
            StoredAccountIdentifier = storedProviderAccount.StoredAccountIdentifier,
            SlotNumber = storedProviderAccount.SlotNumber,
            EmailAddress = storedProviderAccount.EmailAddress,
            DisplayName = storedProviderAccount.DisplayName,
            AccountIdentifier = storedProviderAccount.AccountIdentifier,
            OrganizationIdentifier = storedProviderAccount.OrganizationIdentifier,
            OrganizationName = storedProviderAccount.OrganizationName,
            PlanType = storedProviderAccount.PlanType,
            IsActive = storedProviderAccount.IsActive,
            IsTokenExpired = storedProviderAccount.IsTokenExpired,
            LastUpdated = storedProviderAccount.LastUpdated
        };
}
