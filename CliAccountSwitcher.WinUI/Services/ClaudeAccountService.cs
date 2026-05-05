using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.ClaudeCode;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Authentication;
using CliAccountSwitcher.Api.Security;
using CliAccountSwitcher.Api.Storage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ClaudeAccountService : AccountServiceBase<StoredProviderAccount>
{
    private readonly FileSystemProviderSnapshotStore _providerSnapshotStore;
    private readonly ClaudeCodeProviderAdapter _claudeCodeProviderAdapter;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private bool _disposed;

    public ClaudeAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
        : base(applicationSettingsService, applicationNotificationService)
    {
        _providerSnapshotStore = new FileSystemProviderSnapshotStore(Constants.ProviderSnapshotsDirectory, new WindowsDataProtectionService());
        _claudeCodeProviderAdapter = new ClaudeCodeProviderAdapter(_providerSnapshotStore);
    }

    public override CliProviderKind ProviderKind => CliProviderKind.ClaudeCode;

    public override string BackupFileNamePrefix => "claude-accounts";

    public override bool IsRenameSupported => false;

    public async Task<StoredProviderAccount> SaveCurrentClaudeCodeAccountAsync(CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveCurrentAccountAsync(_providerSnapshotStore, cancellationToken: cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveCurrentClaudeCodeAccountWithoutActivationAsync(CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveCurrentAccountAsync(_providerSnapshotStore, new ProviderStoredAccountSaveOptions { ShouldActivate = false }, cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveClaudeCodeAccountAsync(string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveAccountAsync(_providerSnapshotStore, CreateProviderAccountDocumentSet(credentialsJson, globalConfigJson), cancellationToken: cancellationToken), cancellationToken);

    public async Task<StoredProviderAccount> SaveClaudeCodeAccountWithoutActivationAsync(string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default)
        => await SaveAndRefreshClaudeCodeAccountAsync(_claudeCodeProviderAdapter.SaveAccountAsync(_providerSnapshotStore, CreateProviderAccountDocumentSet(credentialsJson, globalConfigJson), new ProviderStoredAccountSaveOptions { ShouldActivate = false }, cancellationToken), cancellationToken);

    public async Task RunClaudeCodeLoginAsync(CancellationToken cancellationToken = default) => await _claudeCodeProviderAdapter.RunLoginAsync(cancellationToken);

    public override async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var backupDocument = new ClaudeCodeBackupDocument();
        var storedProviderAccounts = GetAccountStatesSnapshot();

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

    public override async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        await using var fileStream = File.OpenRead(backupFilePath);
        var backupDocument = await JsonSerializer.DeserializeAsync(fileStream, CodexAccountJsonSerializerContext.Default.ClaudeCodeBackupDocument, cancellationToken);
        if (backupDocument is null) throw new InvalidDataException("The Claude Code backup document is empty.");
        if (backupDocument.SchemaVersion != 1) throw new InvalidDataException($"Unsupported Claude Code backup schema version: {backupDocument.SchemaVersion}");
        if (!string.Equals(backupDocument.ProviderKind, CliProviderKind.ClaudeCode.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The backup document is not for Claude Code.");

        var importResult = new ProviderAccountBackupImportResult();
        var duplicateKeys = GetAccountStatesSnapshot()
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

        if (importResult.SuccessCount > 0)
        {
            await SynchronizeActiveStatusesAsync(cancellationToken);
            NotifyAccountsChanged();
        }

        return importResult;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveSemaphore.Dispose();
        _claudeCodeProviderAdapter.Dispose();
        base.Dispose();
    }

    protected override ProviderAccount CreateProviderAccount(StoredProviderAccount storedProviderAccount)
        => new()
        {
            ProviderKind = storedProviderAccount.ProviderKind,
            AccountIdentifier = storedProviderAccount.StoredAccountIdentifier,
            ProviderAccountIdentifier = storedProviderAccount.AccountIdentifier,
            AccountDetailText = string.IsNullOrWhiteSpace(storedProviderAccount.AccountIdentifier) ? storedProviderAccount.StoredAccountIdentifier : storedProviderAccount.AccountIdentifier,
            CustomAlias = "",
            DisplayName = GetDisplayName(storedProviderAccount),
            EmailAddress = storedProviderAccount.EmailAddress,
            PlanType = storedProviderAccount.PlanType,
            IsActive = storedProviderAccount.IsActive,
            IsTokenExpired = storedProviderAccount.IsTokenExpired,
            LastProviderUsageSnapshot = CreateProviderUsageSnapshot(storedProviderAccount.ProviderKind, storedProviderAccount.LastProviderUsageSnapshot),
            LastUsageRefreshTime = storedProviderAccount.LastUsageRefreshTime
        };

    protected override string GetAccountIdentifier(StoredProviderAccount storedProviderAccount) => storedProviderAccount.StoredAccountIdentifier;

    protected override string GetDisplayName(StoredProviderAccount storedProviderAccount) => string.IsNullOrWhiteSpace(storedProviderAccount.DisplayName) ? storedProviderAccount.EmailAddress : storedProviderAccount.DisplayName;

    protected override bool GetIsActive(StoredProviderAccount storedProviderAccount) => storedProviderAccount.IsActive;

    protected override void SetIsActive(StoredProviderAccount storedProviderAccount, bool isActive) => storedProviderAccount.IsActive = isActive;

    protected override bool GetIsTokenExpired(StoredProviderAccount storedProviderAccount) => storedProviderAccount.IsTokenExpired;

    protected override void MarkAccountAsExpired(StoredProviderAccount storedProviderAccount)
    {
        storedProviderAccount.IsTokenExpired = true;
        storedProviderAccount.LastUpdated = DateTimeOffset.UtcNow;
    }

    protected override ProviderUsageSnapshot GetProviderUsageSnapshot(StoredProviderAccount storedProviderAccount) => CreateProviderUsageSnapshot(storedProviderAccount.ProviderKind, storedProviderAccount.LastProviderUsageSnapshot);

    protected override async Task<IReadOnlyList<StoredProviderAccount>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken) => await _claudeCodeProviderAdapter.ListStoredAccountsAsync(_providerSnapshotStore, cancellationToken);

    protected override async Task SaveAccountStatesAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            foreach (var storedProviderAccount in GetAccountStatesSnapshot())
            {
                var payloadJson = await _providerSnapshotStore.GetPayloadJsonAsync(CliProviderKind.ClaudeCode, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
                if (string.IsNullOrWhiteSpace(payloadJson)) continue;
                await _providerSnapshotStore.SaveAsync(storedProviderAccount, payloadJson, cancellationToken);
            }
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    protected override async Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentStoredAccountIdentifier = await _claudeCodeProviderAdapter.GetCurrentStoredAccountIdentifierAsync(_providerSnapshotStore, cancellationToken);
            if (!string.IsNullOrWhiteSpace(currentStoredAccountIdentifier)) return currentStoredAccountIdentifier;

            return await _providerSnapshotStore.GetActiveStoredAccountIdentifierAsync(CliProviderKind.ClaudeCode, cancellationToken) ?? "";
        }
        catch { return ""; }
    }

    protected override async Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(StoredProviderAccount storedProviderAccount, CancellationToken cancellationToken)
    {
        var activatedStoredProviderAccount = await _claudeCodeProviderAdapter.ActivateStoredAccountAsync(_providerSnapshotStore, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
        UpsertAccountState(activatedStoredProviderAccount);
        return ProviderActivationFollowUp.RefreshClaudeCodeSession;
    }

    protected override async Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(StoredProviderAccount storedProviderAccount, CancellationToken cancellationToken)
    {
        try
        {
            var providerUsageSnapshot = storedProviderAccount.IsActive
                ? await RefreshActiveAccountUsageCoreAsync(storedProviderAccount, cancellationToken)
                : await _claudeCodeProviderAdapter.GetUsageAsync(storedProviderAccount.StoredAccountIdentifier, cancellationToken);
            var usageRefreshTime = DateTimeOffset.UtcNow;
            storedProviderAccount.IsTokenExpired = false;
            storedProviderAccount.LastUpdated = usageRefreshTime;
            storedProviderAccount.LastProviderUsageSnapshot = CreateProviderUsageSnapshot(CliProviderKind.ClaudeCode, providerUsageSnapshot);
            storedProviderAccount.LastUsageRefreshTime = usageRefreshTime;
            if (!string.IsNullOrWhiteSpace(providerUsageSnapshot.EmailAddress)) storedProviderAccount.EmailAddress = providerUsageSnapshot.EmailAddress;
            if (!string.IsNullOrWhiteSpace(providerUsageSnapshot.PlanType)) storedProviderAccount.PlanType = providerUsageSnapshot.PlanType;
            return storedProviderAccount.LastProviderUsageSnapshot;
        }
        catch (ProviderAuthenticationExpiredException)
        {
            throw;
        }
        catch (ProviderActionRequiredException)
        {
            storedProviderAccount.IsTokenExpired = false;
            storedProviderAccount.LastUpdated = DateTimeOffset.UtcNow;
            return GetProviderUsageSnapshot(storedProviderAccount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return GetProviderUsageSnapshot(storedProviderAccount);
        }
    }

    protected override async Task DeleteAccountStatesCoreAsync(IReadOnlyList<StoredProviderAccount> accountStates, CancellationToken cancellationToken)
    {
        foreach (var storedProviderAccount in accountStates) await _claudeCodeProviderAdapter.DeleteStoredAccountAsync(_providerSnapshotStore, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
    }

    protected override bool IsAccountExpiredException(Exception exception) => exception is ProviderAuthenticationExpiredException;

    private async Task<StoredProviderAccount> SaveAndRefreshClaudeCodeAccountAsync(Task<StoredProviderAccount> saveAccountTask, CancellationToken cancellationToken)
    {
        var storedProviderAccount = await saveAccountTask;
        UpsertAccountState(storedProviderAccount);
        await RefreshAccountsAsync([storedProviderAccount.StoredAccountIdentifier], cancellationToken);
        return FindAccountState(storedProviderAccount.StoredAccountIdentifier) ?? storedProviderAccount;
    }

    private static ProviderAccountDocumentSet CreateProviderAccountDocumentSet(string credentialsJson, string globalConfigJson)
        => new()
        {
            CredentialsDocumentText = credentialsJson,
            GlobalConfigDocumentText = globalConfigJson
        };

    private async Task<ProviderUsageSnapshot> RefreshActiveAccountUsageCoreAsync(StoredProviderAccount storedProviderAccount, CancellationToken cancellationToken)
    {
        var currentStoredAccountIdentifier = await _claudeCodeProviderAdapter.GetCurrentStoredAccountIdentifierAsync(_providerSnapshotStore, cancellationToken);
        if (!string.Equals(currentStoredAccountIdentifier, storedProviderAccount.StoredAccountIdentifier, StringComparison.Ordinal))
        {
            return await _claudeCodeProviderAdapter.GetUsageAsync(storedProviderAccount.StoredAccountIdentifier, cancellationToken);
        }

        var providerUsageSnapshot = await _claudeCodeProviderAdapter.GetUsageAsync(cancellationToken: cancellationToken);
        var updatedStoredProviderAccount = await _claudeCodeProviderAdapter.UpdateStoredAccountFromCurrentLiveAccountAsync(_providerSnapshotStore, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
        if (updatedStoredProviderAccount is not null) ApplyStoredProviderAccountMetadata(storedProviderAccount, updatedStoredProviderAccount);
        return providerUsageSnapshot;
    }

    private static void ApplyStoredProviderAccountMetadata(StoredProviderAccount destinationStoredProviderAccount, StoredProviderAccount sourceStoredProviderAccount)
    {
        destinationStoredProviderAccount.EmailAddress = sourceStoredProviderAccount.EmailAddress;
        destinationStoredProviderAccount.DisplayName = sourceStoredProviderAccount.DisplayName;
        destinationStoredProviderAccount.AccountIdentifier = sourceStoredProviderAccount.AccountIdentifier;
        destinationStoredProviderAccount.OrganizationIdentifier = sourceStoredProviderAccount.OrganizationIdentifier;
        destinationStoredProviderAccount.OrganizationName = sourceStoredProviderAccount.OrganizationName;
        destinationStoredProviderAccount.PlanType = sourceStoredProviderAccount.PlanType;
        destinationStoredProviderAccount.IsTokenExpired = sourceStoredProviderAccount.IsTokenExpired;
        destinationStoredProviderAccount.LastUpdated = sourceStoredProviderAccount.LastUpdated;
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

    private static ProviderUsageSnapshot CreateProviderUsageSnapshot(CliProviderKind providerKind, ProviderUsageSnapshot providerUsageSnapshot)
        => providerUsageSnapshot is null
            ? new ProviderUsageSnapshot { ProviderKind = providerKind }
            : new ProviderUsageSnapshot
            {
                ProviderKind = providerKind,
                PlanType = providerUsageSnapshot.PlanType,
                EmailAddress = providerUsageSnapshot.EmailAddress,
                RawResponseText = providerUsageSnapshot.RawResponseText,
                FiveHour = CreateProviderUsageWindow(providerUsageSnapshot.FiveHour),
                SevenDay = CreateProviderUsageWindow(providerUsageSnapshot.SevenDay)
            };

    private static ProviderUsageWindow CreateProviderUsageWindow(ProviderUsageWindow providerUsageWindow)
        => providerUsageWindow is null
            ? new ProviderUsageWindow()
            : new ProviderUsageWindow
            {
                UsedPercentage = providerUsageWindow.UsedPercentage,
                RemainingPercentage = providerUsageWindow.RemainingPercentage,
                ResetAfterSeconds = providerUsageWindow.ResetAfterSeconds,
                ResetAt = providerUsageWindow.ResetAt
            };
}
