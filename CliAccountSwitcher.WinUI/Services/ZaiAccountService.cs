using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Zai;
using CliAccountSwitcher.Api.Providers.Zai.Infrastructure;
using CliAccountSwitcher.Api.Providers.Zai.Models;
using CliAccountSwitcher.Api.Providers.Zai.Models.Usage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ZaiAccountService : AccountServiceBase<ZaiAccount>
{
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly ZaiUsageClient _zaiUsageClient;
    private string _activeAccountIdentifier = "";
    private bool _disposed;

    public ZaiAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
        : base(applicationSettingsService, applicationNotificationService)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _zaiUsageClient = new ZaiUsageClient(_httpClient);
    }

    public override CliProviderKind ProviderKind => CliProviderKind.Zai;

    public override string BackupFileNamePrefix => "zai-accounts";

    public override bool IsRenameSupported => true;

    public async Task<ZaiAccount> AddApiKeyAsync(string apiKey, string customAlias, bool preferChinaEndpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The Z.ai API key is required.", nameof(apiKey));

        var zaiUsageSnapshot = await _zaiUsageClient.GetUsageAsync(apiKey, preferChinaEndpoint, cancellationToken);
        var zaiAccount = new ZaiAccount
        {
            ApiKey = apiKey.Trim(),
            PreferChinaEndpoint = preferChinaEndpoint || zaiUsageSnapshot.UsedChinaEndpoint,
            CustomAlias = customAlias.Trim(),
            LastZaiUsageSnapshot = zaiUsageSnapshot,
            LastUsageRefreshTime = DateTimeOffset.UtcNow
        };
        zaiAccount.MarkAsValid();

        UpsertAccountState(zaiAccount);
        if (string.IsNullOrWhiteSpace(_activeAccountIdentifier)) _activeAccountIdentifier = zaiAccount.AccountIdentifier;
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
        return zaiAccount;
    }

    public async Task<ZaiAccount> AddChelperConfigAccountAsync(CancellationToken cancellationToken = default)
    {
        var chelperConfig = await ZaiChelperConfigReader.LoadAsync(cancellationToken);
        if (!chelperConfig.IsValid) throw new InvalidOperationException("The chelper config does not contain an API key.");
        return await AddApiKeyAsync(chelperConfig.ApiKey, "", chelperConfig.IsChinaPlan, cancellationToken);
    }

    public override async Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default)
    {
        var zaiAccount = FindAccountState(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        zaiAccount.CustomAlias = customAlias.Trim();
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public override async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
        var zaiAccountStoreDocument = CreateStoreDocumentSnapshot();
        await using var fileStream = File.Create(backupFilePath);
        await JsonSerializer.SerializeAsync(fileStream, zaiAccountStoreDocument, CodexAccountJsonSerializerContext.Default.ZaiAccountStoreDocument, cancellationToken);
    }

    public override async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var hasExistingAccounts = GetAccountStatesSnapshot().Count > 0;
        var providerAccountBackupImportResult = new ProviderAccountBackupImportResult();
        var importedAccountIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var importedAccounts = new List<ZaiAccount>();

        ZaiAccountStoreDocument storeDocument;
        try
        {
            using var fileStream = File.OpenRead(backupFilePath);
            storeDocument = await JsonSerializer.DeserializeAsync(fileStream, CodexAccountJsonSerializerContext.Default.ZaiAccountStoreDocument, cancellationToken) ?? new ZaiAccountStoreDocument();
        }
        catch { return new ProviderAccountBackupImportResult { FailureCount = 1 }; }

        foreach (var candidateAccount in storeDocument.Accounts)
        {
            var accountIdentifier = candidateAccount.AccountIdentifier;
            if (string.IsNullOrWhiteSpace(accountIdentifier) || ContainsAccountState(accountIdentifier) || importedAccountIdentifiers.Contains(accountIdentifier))
            {
                providerAccountBackupImportResult.DuplicateCount++;
                continue;
            }

            UpsertAccountState(candidateAccount);
            importedAccountIdentifiers.Add(accountIdentifier);
            importedAccounts.Add(candidateAccount);
            providerAccountBackupImportResult.SuccessCount++;
        }

        if (providerAccountBackupImportResult.SuccessCount > 0)
        {
            if (!hasExistingAccounts)
            {
                var autoActivateTarget = PickAutoActivateTarget(importedAccounts);
                if (autoActivateTarget is not null) await ActivateAccountCoreAsync(autoActivateTarget, cancellationToken);
            }
            await SynchronizeActiveStatusesAsync(cancellationToken);
            await SaveAccountStatesAsync(cancellationToken);
            NotifyAccountsChanged();
        }

        return providerAccountBackupImportResult;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveSemaphore.Dispose();
        _httpClient.Dispose();
        base.Dispose();
    }

    protected override ProviderAccount CreateProviderAccount(ZaiAccount zaiAccount) => new()
    {
        ProviderKind = CliProviderKind.Zai,
        AccountIdentifier = zaiAccount.AccountIdentifier,
        ProviderAccountIdentifier = zaiAccount.AccountIdentifier,
        AccountDetailText = BuildApiKeyPreview(zaiAccount.ApiKey),
        CustomAlias = zaiAccount.CustomAlias,
        DisplayName = zaiAccount.DisplayName,
        EmailAddress = "",
        PlanType = zaiAccount.PlanType,
        IsActive = zaiAccount.IsActive,
        IsTokenExpired = zaiAccount.IsTokenExpired,
        LastProviderUsageSnapshot = CreateProviderUsageSnapshot(zaiAccount.LastZaiUsageSnapshot),
        LastUsageRefreshTime = zaiAccount.LastUsageRefreshTime
    };

    protected override string GetAccountIdentifier(ZaiAccount zaiAccount) => zaiAccount.AccountIdentifier;

    protected override string GetDisplayName(ZaiAccount zaiAccount) => zaiAccount.DisplayName;

    protected override bool GetIsActive(ZaiAccount zaiAccount) => zaiAccount.IsActive;

    protected override void SetIsActive(ZaiAccount zaiAccount, bool isActive) => zaiAccount.IsActive = isActive;

    protected override bool GetIsTokenExpired(ZaiAccount zaiAccount) => zaiAccount.IsTokenExpired;

    protected override void MarkAccountAsExpired(ZaiAccount zaiAccount) => zaiAccount.MarkAsExpired();

    protected override ProviderUsageSnapshot GetProviderUsageSnapshot(ZaiAccount zaiAccount) => CreateProviderUsageSnapshot(zaiAccount.LastZaiUsageSnapshot);

    protected override DateTimeOffset? GetLastUsageRefreshTime(ZaiAccount zaiAccount) => zaiAccount.LastUsageRefreshTime;

    protected override Task<IReadOnlyList<ZaiAccount>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken)
    {
        var storeDocument = LoadStoreDocument();
        _activeAccountIdentifier = storeDocument.ActiveAccountIdentifier ?? "";
        return Task.FromResult<IReadOnlyList<ZaiAccount>>(storeDocument.Accounts);
    }

    protected override async Task SaveAccountStatesAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Constants.UserDataDirectory);
            var zaiAccountStoreDocument = CreateStoreDocumentSnapshot();
            await using var fileStream = File.Create(Constants.ZaiAccountsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, zaiAccountStoreDocument, CodexAccountJsonSerializerContext.Default.ZaiAccountStoreDocument, cancellationToken);
        }
        finally { _saveSemaphore.Release(); }
    }

    protected override Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken) => Task.FromResult(_activeAccountIdentifier);

    protected override Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(ZaiAccount zaiAccount, CancellationToken cancellationToken)
    {
        _activeAccountIdentifier = GetAccountIdentifier(zaiAccount);
        return Task.FromResult(ProviderActivationFollowUp.None);
    }

    protected override async Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(ZaiAccount zaiAccount, CancellationToken cancellationToken)
    {
        var zaiUsageSnapshot = await _zaiUsageClient.GetUsageAsync(zaiAccount.ApiKey, zaiAccount.PreferChinaEndpoint, cancellationToken);
        if (zaiUsageSnapshot.UsedChinaEndpoint && !zaiAccount.PreferChinaEndpoint) zaiAccount.PreferChinaEndpoint = true;
        zaiAccount.LastZaiUsageSnapshot = zaiUsageSnapshot;
        zaiAccount.LastUsageRefreshTime = DateTimeOffset.UtcNow;
        zaiAccount.MarkAsValid();
        return CreateProviderUsageSnapshot(zaiUsageSnapshot);
    }

    protected override bool IsAccountExpiredException(Exception exception) => exception is ZaiApiException { ApplicationCode: 401 };

    private ZaiAccountStoreDocument LoadStoreDocument()
    {
        try
        {
            if (!File.Exists(Constants.ZaiAccountsFilePath)) return new ZaiAccountStoreDocument();
            using var fileStream = File.OpenRead(Constants.ZaiAccountsFilePath);
            return JsonSerializer.Deserialize(fileStream, CodexAccountJsonSerializerContext.Default.ZaiAccountStoreDocument) ?? new ZaiAccountStoreDocument();
        }
        catch { return new ZaiAccountStoreDocument(); }
    }

    private ZaiAccountStoreDocument CreateStoreDocumentSnapshot() => new()
    {
        Accounts = [..GetAccountStatesSnapshot()],
        ActiveAccountIdentifier = _activeAccountIdentifier ?? ""
    };

    private static ProviderUsageSnapshot CreateProviderUsageSnapshot(ZaiUsageSnapshot zaiUsageSnapshot)
    {
        var snapshot = zaiUsageSnapshot ?? new ZaiUsageSnapshot();
        return new ProviderUsageSnapshot
        {
            ProviderKind = CliProviderKind.Zai,
            PlanType = snapshot.PlanLevel,
            FiveHour = CreateProviderUsageWindow(snapshot.FiveHour),
            SevenDay = CreateProviderUsageWindow(snapshot.SevenDay),
            Monthly = CreateProviderUsageWindow(snapshot.Monthly)
        };
    }

    private static ProviderUsageWindow CreateProviderUsageWindow(ZaiUsageWindow zaiUsageWindow)
    {
        var window = zaiUsageWindow ?? new ZaiUsageWindow();
        return new ProviderUsageWindow
        {
            UsedPercentage = window.UsedPercentage,
            RemainingPercentage = window.RemainingPercentage,
            ResetAfterSeconds = window.ResetAfterSeconds,
            ResetAt = window.ResetAt
        };
    }

    private static string BuildApiKeyPreview(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "";
        return apiKey.Length <= 18 ? apiKey : $"{apiKey[..8]}...{apiKey[^6..]}";
    }
}
