using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Ollama;
using CliAccountSwitcher.Api.Providers.Ollama.Models;
using CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class OllamaAccountService : AccountServiceBase<OllamaAccount>
{
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly OllamaUsageClient _ollamaUsageClient;
    private string _activeAccountIdentifier = "";
    private bool _disposed;

    public OllamaAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
        : base(applicationSettingsService, applicationNotificationService)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ollamaUsageClient = new OllamaUsageClient(_httpClient);
    }

    public override CliProviderKind ProviderKind => CliProviderKind.Ollama;

    public override string BackupFileNamePrefix => "ollama-accounts";

    public override bool IsRenameSupported => true;

    public async Task<OllamaAccount> AddAccountFromWebViewAsync(string authCookie, string userName, string emailAddress, string customAlias, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie)) throw new ArgumentException("The Ollama auth cookie is required.", nameof(authCookie));

        var ollamaUsageSnapshot = await _ollamaUsageClient.GetUsageAsync(authCookie, cancellationToken);
        var ollamaAccount = new OllamaAccount
        {
            AuthCookie = authCookie.Trim(),
            UserName = string.IsNullOrWhiteSpace(userName) ? ollamaUsageSnapshot.UserName : userName.Trim(),
            EmailAddress = string.IsNullOrWhiteSpace(emailAddress) ? ollamaUsageSnapshot.EmailAddress : emailAddress.Trim(),
            CustomAlias = customAlias.Trim(),
            LastOllamaUsageSnapshot = ollamaUsageSnapshot,
            LastUsageRefreshTime = DateTimeOffset.UtcNow,
            AuthCookieObtainedTime = DateTimeOffset.UtcNow
        };
        ollamaAccount.MarkAsValid();

        UpsertAccountState(ollamaAccount);
        if (string.IsNullOrWhiteSpace(_activeAccountIdentifier)) _activeAccountIdentifier = ollamaAccount.AccountIdentifier;
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
        return ollamaAccount;
    }

    public override async Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default)
    {
        var ollamaAccount = FindAccountState(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        ollamaAccount.CustomAlias = customAlias.Trim();
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public override async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
        var ollamaAccountStoreDocument = CreateStoreDocumentSnapshot();
        await using var fileStream = File.Create(backupFilePath);
        await JsonSerializer.SerializeAsync(fileStream, ollamaAccountStoreDocument, CodexAccountJsonSerializerContext.Default.OllamaAccountStoreDocument, cancellationToken);
    }

    public override async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var hasExistingAccounts = GetAccountStatesSnapshot().Count > 0;
        var providerAccountBackupImportResult = new ProviderAccountBackupImportResult();
        var importedAccountIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var importedAccounts = new List<OllamaAccount>();

        OllamaAccountStoreDocument storeDocument;
        try
        {
            using var fileStream = File.OpenRead(backupFilePath);
            storeDocument = await JsonSerializer.DeserializeAsync(fileStream, CodexAccountJsonSerializerContext.Default.OllamaAccountStoreDocument, cancellationToken) ?? new OllamaAccountStoreDocument();
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

    protected override ProviderAccount CreateProviderAccount(OllamaAccount ollamaAccount) => new()
    {
        ProviderKind = CliProviderKind.Ollama,
        AccountIdentifier = ollamaAccount.AccountIdentifier,
        ProviderAccountIdentifier = ollamaAccount.AccountIdentifier,
        AccountDetailText = BuildAuthCookiePreview(ollamaAccount.AuthCookie),
        CustomAlias = ollamaAccount.CustomAlias,
        DisplayName = ollamaAccount.DisplayName,
        EmailAddress = ollamaAccount.EmailAddress,
        PlanType = ollamaAccount.PlanType,
        IsActive = ollamaAccount.IsActive,
        IsTokenExpired = ollamaAccount.IsTokenExpired,
        LastProviderUsageSnapshot = CreateProviderUsageSnapshot(ollamaAccount.LastOllamaUsageSnapshot),
        LastUsageRefreshTime = ollamaAccount.LastUsageRefreshTime
    };

    protected override string GetAccountIdentifier(OllamaAccount ollamaAccount) => ollamaAccount.AccountIdentifier;

    protected override string GetDisplayName(OllamaAccount ollamaAccount) => ollamaAccount.DisplayName;

    protected override bool GetIsActive(OllamaAccount ollamaAccount) => ollamaAccount.IsActive;

    protected override void SetIsActive(OllamaAccount ollamaAccount, bool isActive) => ollamaAccount.IsActive = isActive;

    protected override bool GetIsTokenExpired(OllamaAccount ollamaAccount) => ollamaAccount.IsTokenExpired;

    protected override void MarkAccountAsExpired(OllamaAccount ollamaAccount) => ollamaAccount.MarkAsExpired();

    protected override ProviderUsageSnapshot GetProviderUsageSnapshot(OllamaAccount ollamaAccount) => CreateProviderUsageSnapshot(ollamaAccount.LastOllamaUsageSnapshot);

    protected override DateTimeOffset? GetLastUsageRefreshTime(OllamaAccount ollamaAccount) => ollamaAccount.LastUsageRefreshTime;

    protected override Task<IReadOnlyList<OllamaAccount>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken)
    {
        var storeDocument = LoadStoreDocument();
        _activeAccountIdentifier = storeDocument.ActiveAccountIdentifier ?? "";
        return Task.FromResult<IReadOnlyList<OllamaAccount>>(storeDocument.Accounts);
    }

    protected override async Task SaveAccountStatesAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Constants.UserDataDirectory);
            var ollamaAccountStoreDocument = CreateStoreDocumentSnapshot();
            await using var fileStream = File.Create(Constants.OllamaAccountsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, ollamaAccountStoreDocument, CodexAccountJsonSerializerContext.Default.OllamaAccountStoreDocument, cancellationToken);
        }
        finally { _saveSemaphore.Release(); }
    }

    protected override Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken) => Task.FromResult(_activeAccountIdentifier);

    protected override Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(OllamaAccount ollamaAccount, CancellationToken cancellationToken)
    {
        _activeAccountIdentifier = GetAccountIdentifier(ollamaAccount);
        return Task.FromResult(ProviderActivationFollowUp.None);
    }

    protected override async Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(OllamaAccount ollamaAccount, CancellationToken cancellationToken)
    {
        var ollamaUsageSnapshot = await _ollamaUsageClient.GetUsageAsync(ollamaAccount.AuthCookie, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ollamaUsageSnapshot.UserName) && string.IsNullOrWhiteSpace(ollamaAccount.UserName)) ollamaAccount.UserName = ollamaUsageSnapshot.UserName;
        if (!string.IsNullOrWhiteSpace(ollamaUsageSnapshot.EmailAddress) && string.IsNullOrWhiteSpace(ollamaAccount.EmailAddress)) ollamaAccount.EmailAddress = ollamaUsageSnapshot.EmailAddress;
        ollamaAccount.LastOllamaUsageSnapshot = ollamaUsageSnapshot;
        ollamaAccount.LastUsageRefreshTime = DateTimeOffset.UtcNow;
        ollamaAccount.MarkAsValid();
        return CreateProviderUsageSnapshot(ollamaUsageSnapshot);
    }

    protected override bool IsAccountExpiredException(Exception exception) => exception is OllamaAuthExpiredException;

    private OllamaAccountStoreDocument LoadStoreDocument()
    {
        try
        {
            if (!File.Exists(Constants.OllamaAccountsFilePath)) return new OllamaAccountStoreDocument();
            using var fileStream = File.OpenRead(Constants.OllamaAccountsFilePath);
            return JsonSerializer.Deserialize(fileStream, CodexAccountJsonSerializerContext.Default.OllamaAccountStoreDocument) ?? new OllamaAccountStoreDocument();
        }
        catch { return new OllamaAccountStoreDocument(); }
    }

    private OllamaAccountStoreDocument CreateStoreDocumentSnapshot() => new()
    {
        Accounts = [..GetAccountStatesSnapshot()],
        ActiveAccountIdentifier = _activeAccountIdentifier ?? ""
    };

    private static ProviderUsageSnapshot CreateProviderUsageSnapshot(OllamaUsageSnapshot ollamaUsageSnapshot)
    {
        var snapshot = ollamaUsageSnapshot ?? new OllamaUsageSnapshot();
        return new ProviderUsageSnapshot
        {
            ProviderKind = CliProviderKind.Ollama,
            PlanType = snapshot.PlanLevel,
            EmailAddress = snapshot.EmailAddress,
            FiveHour = CreateProviderUsageWindow(snapshot.SessionUsage),
            SevenDay = CreateProviderUsageWindow(snapshot.WeeklyUsage),
            Monthly = new ProviderUsageWindow()
        };
    }

    private static ProviderUsageWindow CreateProviderUsageWindow(OllamaUsageWindow ollamaUsageWindow)
    {
        var window = ollamaUsageWindow ?? new OllamaUsageWindow();
        return new ProviderUsageWindow
        {
            UsedPercentage = window.UsedPercentage,
            RemainingPercentage = window.RemainingPercentage,
            ResetAfterSeconds = window.ResetAfterSeconds,
            ResetAt = window.ResetAt
        };
    }

    private static string BuildAuthCookiePreview(string authCookie)
    {
        if (string.IsNullOrWhiteSpace(authCookie)) return "";
        return authCookie.Length <= 18 ? authCookie : $"{authCookie[..8]}...{authCookie[^6..]}";
    }
}