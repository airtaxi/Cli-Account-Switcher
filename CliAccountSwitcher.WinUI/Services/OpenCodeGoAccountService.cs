using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.OpenCodeGo;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models.Usage;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class OpenCodeGoAccountService : AccountServiceBase<OpenCodeGoAccount>
{
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly OpenCodeGoUsageClient _openCodeGoUsageClient;
    private readonly OpenCodeGoKeysClient _openCodeGoKeysClient;
    private readonly OpenCodeGoAuthValidator _openCodeGoAuthValidator;
    private string _activeAccountIdentifier = "";
    private bool _disposed;

    public OpenCodeGoAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
        : base(applicationSettingsService, applicationNotificationService)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _openCodeGoUsageClient = new OpenCodeGoUsageClient(_httpClient);
        _openCodeGoKeysClient = new OpenCodeGoKeysClient(_httpClient);
        _openCodeGoAuthValidator = new OpenCodeGoAuthValidator(_httpClient);
    }

    public override CliProviderKind ProviderKind => CliProviderKind.OpenCodeGo;

    public override string BackupFileNamePrefix => "opencode-accounts";

    public override bool IsRenameSupported => true;

    public async Task<OpenCodeGoAccount> AddAccountFromWebViewAsync(string authCookie, string workspaceId, string apiKey, string apiKeyDisplayName, string emailAddress, string customAlias, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The OpenCode Go API key is required.", nameof(apiKey));

        var openCodeGoUsageSnapshot = await FetchUsageSnapshotAsync(workspaceId, authCookie, cancellationToken);
        var openCodeGoAccount = new OpenCodeGoAccount
        {
            ApiKey = apiKey.Trim(),
            AuthCookie = authCookie,
            WorkspaceId = workspaceId,
            ApiKeyDisplayName = apiKeyDisplayName,
            EmailAddress = emailAddress,
            CustomAlias = customAlias.Trim(),
            LastOpenCodeGoUsageSnapshot = openCodeGoUsageSnapshot,
            LastUsageRefreshTime = DateTimeOffset.UtcNow,
            AuthCookieObtainedTime = DateTimeOffset.UtcNow
        };
        openCodeGoAccount.MarkAsValid();

        UpsertAccountState(openCodeGoAccount);
        if (string.IsNullOrWhiteSpace(_activeAccountIdentifier)) _activeAccountIdentifier = openCodeGoAccount.AccountIdentifier;
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
        return openCodeGoAccount;
    }

    public async Task<OpenCodeGoKeyInfo> TryFetchApiKeyAsync(string authCookie, string workspaceId, CancellationToken cancellationToken = default)
    {
        return await _openCodeGoKeysClient.GetFirstApiKeyAsync(workspaceId, authCookie, cancellationToken);
    }

    public async Task<bool> IsAuthCookieValidAsync(string authCookie, CancellationToken cancellationToken = default) => await _openCodeGoAuthValidator.IsAuthCookieValidAsync(authCookie, cancellationToken);

    public async Task<string> GetAccountEmailAsync(string authCookie, CancellationToken cancellationToken = default) => await _openCodeGoAuthValidator.GetAccountEmailAsync(authCookie, cancellationToken);

    public override async Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default)
    {
        var openCodeGoAccount = FindAccountState(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        openCodeGoAccount.CustomAlias = customAlias.Trim();
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public override async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
        var openCodeGoAccountStoreDocument = CreateStoreDocumentSnapshot();
        await using var fileStream = File.Create(backupFilePath);
        await JsonSerializer.SerializeAsync(fileStream, openCodeGoAccountStoreDocument, CodexAccountJsonSerializerContext.Default.OpenCodeGoAccountStoreDocument, cancellationToken);
    }

    public override async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var providerAccountBackupImportResult = new ProviderAccountBackupImportResult();
        var importedAccountIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        OpenCodeGoAccountStoreDocument storeDocument;
        try
        {
            using var fileStream = File.OpenRead(backupFilePath);
            storeDocument = await JsonSerializer.DeserializeAsync(fileStream, CodexAccountJsonSerializerContext.Default.OpenCodeGoAccountStoreDocument, cancellationToken) ?? new OpenCodeGoAccountStoreDocument();
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
            providerAccountBackupImportResult.SuccessCount++;
        }

        if (providerAccountBackupImportResult.SuccessCount > 0)
        {
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

    protected override ProviderAccount CreateProviderAccount(OpenCodeGoAccount openCodeGoAccount) => new()
    {
        ProviderKind = CliProviderKind.OpenCodeGo,
        AccountIdentifier = openCodeGoAccount.AccountIdentifier,
        ProviderAccountIdentifier = openCodeGoAccount.AccountIdentifier,
        AccountDetailText = BuildApiKeyPreview(openCodeGoAccount.ApiKey),
        CustomAlias = openCodeGoAccount.CustomAlias,
        DisplayName = openCodeGoAccount.DisplayName,
        EmailAddress = openCodeGoAccount.EmailAddress,
        PlanType = openCodeGoAccount.PlanType,
        IsActive = openCodeGoAccount.IsActive,
        IsTokenExpired = openCodeGoAccount.IsTokenExpired,
        LastProviderUsageSnapshot = CreateProviderUsageSnapshot(openCodeGoAccount.LastOpenCodeGoUsageSnapshot),
        LastUsageRefreshTime = openCodeGoAccount.LastUsageRefreshTime
    };

    protected override string GetAccountIdentifier(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.AccountIdentifier;

    protected override string GetDisplayName(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.DisplayName;

    protected override bool GetIsActive(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.IsActive;

    protected override void SetIsActive(OpenCodeGoAccount openCodeGoAccount, bool isActive) => openCodeGoAccount.IsActive = isActive;

    protected override bool GetIsTokenExpired(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.IsTokenExpired;

    protected override void MarkAccountAsExpired(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.MarkAsExpired();

    protected override ProviderUsageSnapshot GetProviderUsageSnapshot(OpenCodeGoAccount openCodeGoAccount) => CreateProviderUsageSnapshot(openCodeGoAccount.LastOpenCodeGoUsageSnapshot);

    protected override DateTimeOffset? GetLastUsageRefreshTime(OpenCodeGoAccount openCodeGoAccount) => openCodeGoAccount.LastUsageRefreshTime;

    protected override Task<IReadOnlyList<OpenCodeGoAccount>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken)
    {
        var storeDocument = LoadStoreDocument();
        _activeAccountIdentifier = storeDocument.ActiveAccountIdentifier ?? "";
        return Task.FromResult<IReadOnlyList<OpenCodeGoAccount>>(storeDocument.Accounts);
    }

    protected override async Task SaveAccountStatesAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Constants.UserDataDirectory);
            var openCodeGoAccountStoreDocument = CreateStoreDocumentSnapshot();
            await using var fileStream = File.Create(Constants.OpenCodeGoAccountsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, openCodeGoAccountStoreDocument, CodexAccountJsonSerializerContext.Default.OpenCodeGoAccountStoreDocument, cancellationToken);
        }
        finally { _saveSemaphore.Release(); }
    }

    protected override Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken) => Task.FromResult(_activeAccountIdentifier);

    protected override async Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(OpenCodeGoAccount openCodeGoAccount, CancellationToken cancellationToken)
    {
        await WriteAuthJsonAtomicallyAsync(openCodeGoAccount.ApiKey, cancellationToken);
        _activeAccountIdentifier = GetAccountIdentifier(openCodeGoAccount);
        return ProviderActivationFollowUp.RestartOpenCodeGo;
    }

    protected override async Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(OpenCodeGoAccount openCodeGoAccount, CancellationToken cancellationToken)
    {
        var openCodeGoUsageSnapshot = await FetchUsageSnapshotAsync(openCodeGoAccount.WorkspaceId, openCodeGoAccount.AuthCookie, cancellationToken);
        openCodeGoAccount.LastOpenCodeGoUsageSnapshot = openCodeGoUsageSnapshot;
        openCodeGoAccount.LastUsageRefreshTime = DateTimeOffset.UtcNow;
        openCodeGoAccount.MarkAsValid();
        return CreateProviderUsageSnapshot(openCodeGoUsageSnapshot);
    }

    protected override bool IsAccountExpiredException(Exception exception) => exception is OpenCodeGoAuthExpiredException;

    private async Task<OpenCodeGoUsageSnapshot> FetchUsageSnapshotAsync(string workspaceId, string authCookie, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(authCookie)) return new OpenCodeGoUsageSnapshot();
        return await _openCodeGoUsageClient.GetUsageAsync(workspaceId, authCookie, cancellationToken);
    }

    private static async Task WriteAuthJsonAtomicallyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var authFilePath = Constants.OpenCodeGoAuthFilePath;
        var directoryPath = Path.GetDirectoryName(authFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);

        var existingJson = await TryReadAllTextAsync(authFilePath, cancellationToken);
        var updatedJson = UpdateAuthJsonWithGoApiKey(existingJson, apiKey);

        var temporaryFilePath = $"{authFilePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryFilePath, updatedJson, cancellationToken);
        File.Move(temporaryFilePath, authFilePath, true);
    }

    private static async Task<string> TryReadAllTextAsync(string filePath, CancellationToken cancellationToken)
    {
        try { return await File.ReadAllTextAsync(filePath, cancellationToken); }
        catch { return null; }
    }

    private static string UpdateAuthJsonWithGoApiKey(string existingJson, string apiKey)
    {
        using var jsonDocument = existingJson is not null && existingJson.Trim().Length > 0 ? JsonDocument.Parse(existingJson) : null;
        var writerOptions = new JsonWriterOptions { Indented = true };
        using var memoryStream = new MemoryStream();
        using (var utf8JsonWriter = new Utf8JsonWriter(memoryStream, writerOptions))
        {
            utf8JsonWriter.WriteStartObject();
            if (jsonDocument is not null)
            {
                foreach (var property in jsonDocument.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, OpenCodeGoApiConventions.ProviderId, StringComparison.Ordinal)) continue;
                    property.WriteTo(utf8JsonWriter);
                }
            }

            utf8JsonWriter.WritePropertyName(OpenCodeGoApiConventions.ProviderId);
            utf8JsonWriter.WriteStartObject();
            utf8JsonWriter.WriteString("type", "api");
            utf8JsonWriter.WriteString("key", apiKey);
            utf8JsonWriter.WriteEndObject();

            utf8JsonWriter.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    private OpenCodeGoAccountStoreDocument LoadStoreDocument()
    {
        try
        {
            if (!File.Exists(Constants.OpenCodeGoAccountsFilePath)) return new OpenCodeGoAccountStoreDocument();
            using var fileStream = File.OpenRead(Constants.OpenCodeGoAccountsFilePath);
            return JsonSerializer.Deserialize(fileStream, CodexAccountJsonSerializerContext.Default.OpenCodeGoAccountStoreDocument) ?? new OpenCodeGoAccountStoreDocument();
        }
        catch { return new OpenCodeGoAccountStoreDocument(); }
    }

    private OpenCodeGoAccountStoreDocument CreateStoreDocumentSnapshot() => new()
    {
        Accounts = [..GetAccountStatesSnapshot()],
        ActiveAccountIdentifier = _activeAccountIdentifier ?? ""
    };

    private static ProviderUsageSnapshot CreateProviderUsageSnapshot(OpenCodeGoUsageSnapshot openCodeGoUsageSnapshot)
    {
        var snapshot = openCodeGoUsageSnapshot ?? new OpenCodeGoUsageSnapshot();
        return new ProviderUsageSnapshot
        {
            ProviderKind = CliProviderKind.OpenCodeGo,
            PlanType = snapshot.PlanLevel,
            FiveHour = CreateProviderUsageWindow(snapshot.RollingUsage),
            SevenDay = CreateProviderUsageWindow(snapshot.WeeklyUsage),
            Monthly = CreateProviderUsageWindow(snapshot.MonthlyUsage)
        };
    }

    private static ProviderUsageWindow CreateProviderUsageWindow(OpenCodeGoUsageWindow openCodeGoUsageWindow)
    {
        var window = openCodeGoUsageWindow ?? new OpenCodeGoUsageWindow();
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
