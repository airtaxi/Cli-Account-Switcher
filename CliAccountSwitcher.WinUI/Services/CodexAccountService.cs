using CliAccountSwitcher.Api.Providers.Codex.Authentication;
using CliAccountSwitcher.Api.Providers.Codex;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure;
using CliAccountSwitcher.Api.Providers.Codex.Models;
using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Models.OAuth;
using CliAccountSwitcher.Api.Providers.Codex.Models.Usage;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class CodexAccountService : AccountServiceBase<CodexAccount>
{
    private const int MaximumRefreshTokenFailureCount = 3;

    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly CodexAuthenticationDocumentSerializer _codexAuthenticationDocumentSerializer = new();
    private readonly CodexOAuthClient _codexOAuthClient;
    private readonly CodexUsageClient _codexUsageClient;
    private FileSystemWatcher _authenticationFileSystemWatcher;
    private bool _disposed;

    public CodexAccountService(ApplicationSettingsService applicationSettingsService, ApplicationNotificationService applicationNotificationService)
        : base(applicationSettingsService, applicationNotificationService)
    {
        var codexApiClientOptions = new CodexApiClientOptions
        {
            CodexHomeDirectoryPath = Constants.CodexHomeDirectory
        };
        var codexClientMetadataProvider = new CodexClientMetadataProvider(codexApiClientOptions);
        var codexRequestMessageFactory = new CodexRequestMessageFactory(codexApiClientOptions, codexClientMetadataProvider);

        _httpClient = CodexHttpClientFactory.CreateDefault();
        _codexOAuthClient = new CodexOAuthClient(_httpClient, codexApiClientOptions, codexRequestMessageFactory);
        _codexUsageClient = new CodexUsageClient(_httpClient, codexRequestMessageFactory);
    }

    public override CliProviderKind ProviderKind => CliProviderKind.Codex;

    public override string BackupFileNamePrefix => "codex-accounts";

    public override bool IsRenameSupported => true;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(cancellationToken);
        StartAuthenticationFileSystemWatcher();
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
        UpsertAccountState(codexAccount);
        await SynchronizeActiveStatusesAsync(cancellationToken);
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
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

    public override async Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default)
    {
        var codexAccount = FindAccountState(accountIdentifier) ?? throw new InvalidOperationException("The account does not exist.");
        codexAccount.CustomAlias = customAlias.Trim();
        await SaveAccountStatesAsync(cancellationToken);
        NotifyAccountsChanged();
    }

    public override async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
        using var zipArchive = ZipFile.Open(backupFilePath, ZipArchiveMode.Create);
        var zipArchiveEntry = zipArchive.CreateEntry("accounts.json", CompressionLevel.Optimal);
        await using var zipArchiveEntryStream = zipArchiveEntry.Open();
        var codexAccountStoreDocument = CreateStoreDocumentSnapshot();
        await JsonSerializer.SerializeAsync(zipArchiveEntryStream, codexAccountStoreDocument, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument, cancellationToken);
    }

    public override async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var providerAccountBackupImportResult = new ProviderAccountBackupImportResult();
        var importedAccountIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        using var zipArchive = ZipFile.OpenRead(backupFilePath);
        foreach (var zipArchiveEntry in zipArchive.Entries.Where(zipArchiveEntry => string.Equals(Path.GetExtension(zipArchiveEntry.FullName), ".json", StringComparison.OrdinalIgnoreCase)))
        {
            var candidateAccounts = await ReadBackupEntryAccountsAsync(zipArchiveEntry, cancellationToken);
            if (candidateAccounts.Count == 0)
            {
                providerAccountBackupImportResult.FailureCount++;
                continue;
            }

            foreach (var candidateAccount in candidateAccounts)
            {
                var accountIdentifier = TryGetAccountIdentifier(candidateAccount.CodexAuthenticationDocument);
                if (string.IsNullOrWhiteSpace(accountIdentifier))
                {
                    providerAccountBackupImportResult.FailureCount++;
                    continue;
                }

                if (ContainsAccountState(accountIdentifier) || importedAccountIdentifiers.Contains(accountIdentifier))
                {
                    providerAccountBackupImportResult.DuplicateCount++;
                    continue;
                }

                try
                {
                    var validatedAccount = await CreateValidatedAccountAsync(candidateAccount.CodexAuthenticationDocument, candidateAccount.CustomAlias, cancellationToken);
                    UpsertAccountState(validatedAccount);
                    importedAccountIdentifiers.Add(validatedAccount.AccountIdentifier);
                    providerAccountBackupImportResult.SuccessCount++;
                }
                catch { providerAccountBackupImportResult.FailureCount++; }
            }
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
        _authenticationFileSystemWatcher?.Dispose();
        _saveSemaphore.Dispose();
        _httpClient.Dispose();
        base.Dispose();
    }

    protected override ProviderAccount CreateProviderAccount(CodexAccount codexAccount) => new()
    {
        ProviderKind = CliProviderKind.Codex,
        AccountIdentifier = codexAccount.AccountIdentifier,
        ProviderAccountIdentifier = codexAccount.CodexAuthenticationDocument.GetEffectiveAccountIdentifier(),
        AccountDetailText = BuildAccessTokenPreview(codexAccount.CodexAuthenticationDocument.GetEffectiveAccessToken()),
        CustomAlias = codexAccount.CustomAlias,
        DisplayName = codexAccount.DisplayName,
        EmailAddress = codexAccount.EmailAddress,
        PlanType = codexAccount.PlanType,
        IsActive = codexAccount.IsActive,
        IsTokenExpired = codexAccount.IsTokenExpired,
        LastProviderUsageSnapshot = CreateProviderUsageSnapshot(codexAccount.LastCodexUsageSnapshot),
        LastUsageRefreshTime = codexAccount.LastUsageRefreshTime
    };

    protected override string GetAccountIdentifier(CodexAccount codexAccount) => codexAccount.AccountIdentifier;

    protected override string GetDisplayName(CodexAccount codexAccount) => codexAccount.DisplayName;

    protected override bool GetIsActive(CodexAccount codexAccount) => codexAccount.IsActive;

    protected override void SetIsActive(CodexAccount codexAccount, bool isActive) => codexAccount.IsActive = isActive;

    protected override bool GetIsTokenExpired(CodexAccount codexAccount) => codexAccount.IsTokenExpired;

    protected override void MarkAccountAsExpired(CodexAccount codexAccount) => codexAccount.MarkAsExpired();

    protected override ProviderUsageSnapshot GetProviderUsageSnapshot(CodexAccount codexAccount) => CreateProviderUsageSnapshot(codexAccount.LastCodexUsageSnapshot);

    protected override DateTimeOffset? GetLastUsageRefreshTime(CodexAccount codexAccount) => codexAccount.LastUsageRefreshTime;

    protected override Task<IReadOnlyList<CodexAccount>> LoadAccountStatesCoreAsync(CancellationToken cancellationToken) => Task.FromResult(LoadAccounts());

    protected override async Task SaveAccountStatesAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Constants.UserDataDirectory);
            var codexAccountStoreDocument = CreateStoreDocumentSnapshot();
            await using var fileStream = File.Create(Constants.AccountsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, codexAccountStoreDocument, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument, cancellationToken);
        }
        finally { _saveSemaphore.Release(); }
    }

    protected override async Task<string> ReadActiveAccountIdentifierAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(Constants.CurrentAuthenticationFilePath)) return "";
            var authenticationDocumentText = await File.ReadAllTextAsync(Constants.CurrentAuthenticationFilePath, cancellationToken);
            var codexAuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
            return codexAuthenticationDocument.GetEffectiveProfileIdentifier();
        }
        catch { return ""; }
    }

    protected override async Task<ProviderActivationFollowUp> ActivateAccountCoreAsync(CodexAccount codexAccount, CancellationToken cancellationToken)
    {
        var authenticationDocumentText = _codexAuthenticationDocumentSerializer.Serialize(codexAccount.CodexAuthenticationDocument);
        Directory.CreateDirectory(Constants.CodexHomeDirectory);
        await File.WriteAllTextAsync(Constants.CurrentAuthenticationFilePath, authenticationDocumentText, cancellationToken);
        return ProviderActivationFollowUp.RestartCodex;
    }

    protected override async Task<ProviderUsageSnapshot> RefreshAccountUsageCoreAsync(CodexAccount codexAccount, CancellationToken cancellationToken)
    {
        var activeAuthenticationDocumentUsageSnapshot = await TryRefreshActiveAccountUsageFromCurrentAuthenticationDocumentAsync(codexAccount, cancellationToken);
        if (activeAuthenticationDocumentUsageSnapshot is not null) return activeAuthenticationDocumentUsageSnapshot;

        var refreshResult = await GetUsageWithRefreshTokenRetryAsync(codexAccount, codexAccount.CodexAuthenticationDocument, codexAccount.IsActive, cancellationToken);
        codexAccount.CodexAuthenticationDocument = refreshResult.AuthenticationDocument;
        var codexUsageSnapshot = refreshResult.UsageSnapshot;
        ApplySuccessfulUsageRefresh(codexAccount, codexUsageSnapshot);
        return CreateProviderUsageSnapshot(codexUsageSnapshot);
    }

    protected override bool IsAccountExpiredException(Exception exception) => exception is CodexApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden };

    private async Task<ProviderUsageSnapshot> TryRefreshActiveAccountUsageFromCurrentAuthenticationDocumentAsync(CodexAccount codexAccount, CancellationToken cancellationToken)
    {
        if (!codexAccount.IsActive) return null;

        var currentAuthenticationDocument = await TryReadCurrentAuthenticationDocumentAsync(cancellationToken);
        if (currentAuthenticationDocument is null) return null;

        var currentAccountIdentifier = TryGetAccountIdentifier(currentAuthenticationDocument);
        if (string.IsNullOrWhiteSpace(currentAccountIdentifier) || !string.Equals(currentAccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal)) return null;
        if (!HasAuthenticationTokenInfoChanged(codexAccount.CodexAuthenticationDocument, currentAuthenticationDocument)) return null;

        var refreshResult = await GetUsageWithRefreshTokenRetryAsync(codexAccount, currentAuthenticationDocument, true, cancellationToken);
        codexAccount.CodexAuthenticationDocument = refreshResult.AuthenticationDocument;
        var codexUsageSnapshot = refreshResult.UsageSnapshot;
        ApplySuccessfulUsageRefresh(codexAccount, codexUsageSnapshot);
        return CreateProviderUsageSnapshot(codexUsageSnapshot);
    }

    private async Task<(CodexAuthenticationDocument AuthenticationDocument, CodexUsageSnapshot UsageSnapshot)> GetUsageWithRefreshTokenRetryAsync(CodexAccount codexAccount, CodexAuthenticationDocument authenticationDocument, bool shouldWriteCurrentAuthenticationDocument, CancellationToken cancellationToken)
    {
        try
        {
            var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(authenticationDocument, cancellationToken);
            return (authenticationDocument, codexUsageSnapshot);
        }
        catch (Exception exception) when (IsAccountExpiredException(exception))
        {
            if (codexAccount.RefreshTokenFailureCount >= MaximumRefreshTokenFailureCount) throw new CodexApiException("The Codex refresh token retry limit has been reached. Login again or re-save the account.", HttpStatusCode.Unauthorized, null, exception);

            return await RefreshAuthenticationDocumentAndGetUsageWithRetryAsync(codexAccount, authenticationDocument, shouldWriteCurrentAuthenticationDocument, exception, cancellationToken);
        }
    }

    private async Task<(CodexAuthenticationDocument AuthenticationDocument, CodexUsageSnapshot UsageSnapshot)> RefreshAuthenticationDocumentAndGetUsageWithRetryAsync(CodexAccount codexAccount, CodexAuthenticationDocument authenticationDocument, bool shouldWriteCurrentAuthenticationDocument, Exception expiredException, CancellationToken cancellationToken)
    {
        var currentAuthenticationDocument = authenticationDocument;
        var lastRefreshTokenException = expiredException;

        while (codexAccount.RefreshTokenFailureCount < MaximumRefreshTokenFailureCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                currentAuthenticationDocument = await RefreshAuthenticationDocumentAsync(currentAuthenticationDocument, cancellationToken);
                if (shouldWriteCurrentAuthenticationDocument) await WriteCurrentAuthenticationDocumentAsync(currentAuthenticationDocument, cancellationToken);

                var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(currentAuthenticationDocument, cancellationToken);
                return (currentAuthenticationDocument, codexUsageSnapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRefreshTokenFailureException(exception))
            {
                lastRefreshTokenException = exception;
                codexAccount.RefreshTokenFailureCount++;
            }
        }

        throw new CodexApiException("The Codex refresh token retry limit has been reached. Login again or re-save the account.", HttpStatusCode.Unauthorized, null, lastRefreshTokenException);
    }

    private async Task<CodexAuthenticationDocument> RefreshAuthenticationDocumentAsync(CodexAuthenticationDocument authenticationDocument, CancellationToken cancellationToken)
    {
        var refreshToken = authenticationDocument.GetEffectiveRefreshToken();
        var tokenExchangeResult = await _codexOAuthClient.ExchangeRefreshTokenAsync(refreshToken, cancellationToken);
        var refreshedAuthenticationDocument = CodexOAuthClient.CreateAuthenticationDocument(tokenExchangeResult);
        refreshedAuthenticationDocument.AuthenticationMode = authenticationDocument.AuthenticationMode;
        refreshedAuthenticationDocument.OpenAiApiKey = authenticationDocument.OpenAiApiKey;
        refreshedAuthenticationDocument.AuthenticationType = authenticationDocument.AuthenticationType;
        if (string.IsNullOrWhiteSpace(tokenExchangeResult.EmailAddress)) refreshedAuthenticationDocument.EmailAddress = authenticationDocument.EmailAddress;
        if (!string.IsNullOrWhiteSpace(refreshedAuthenticationDocument.GetEffectiveRefreshToken())) return refreshedAuthenticationDocument;

        refreshedAuthenticationDocument.Tokens.RefreshToken = refreshToken;
        refreshedAuthenticationDocument.RefreshToken = refreshToken;
        return refreshedAuthenticationDocument;
    }

    private async Task WriteCurrentAuthenticationDocumentAsync(CodexAuthenticationDocument authenticationDocument, CancellationToken cancellationToken)
    {
        var authenticationDocumentText = _codexAuthenticationDocumentSerializer.Serialize(authenticationDocument);
        Directory.CreateDirectory(Constants.CodexHomeDirectory);
        await File.WriteAllTextAsync(Constants.CurrentAuthenticationFilePath, authenticationDocumentText, cancellationToken);
    }

    private async Task<CodexAuthenticationDocument> TryReadCurrentAuthenticationDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(Constants.CurrentAuthenticationFilePath)) return null;

            var authenticationDocumentText = await File.ReadAllTextAsync(Constants.CurrentAuthenticationFilePath, cancellationToken);
            return CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static void ApplySuccessfulUsageRefresh(CodexAccount codexAccount, CodexUsageSnapshot codexUsageSnapshot)
    {
        codexAccount.LastCodexUsageSnapshot = codexUsageSnapshot;
        codexAccount.LastUsageRefreshTime = DateTimeOffset.UtcNow;
        codexAccount.RefreshTokenFailureCount = 0;
        codexAccount.MarkAsValid();
        UpdateEmailAddress(codexAccount, codexUsageSnapshot);
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

    private IReadOnlyList<CodexAccount> LoadAccounts()
    {
        try
        {
            if (!File.Exists(Constants.AccountsFilePath)) return [];

            using var fileStream = File.OpenRead(Constants.AccountsFilePath);
            var codexAccountStoreDocument = JsonSerializer.Deserialize(fileStream, CodexAccountJsonSerializerContext.Default.CodexAccountStoreDocument);
            return codexAccountStoreDocument?.Accounts?.Where(account => !string.IsNullOrWhiteSpace(TryGetAccountIdentifier(account.CodexAuthenticationDocument))).ToArray() ?? [];
        }
        catch { return []; }
    }

    private CodexAccountStoreDocument CreateStoreDocumentSnapshot() => new()
    {
        Accounts = [..GetAccountStatesSnapshot()]
    };

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

    private void OnAuthenticationFileSystemWatcherChanged(object sender, FileSystemEventArgs fileSystemEventArguments) => _ = SynchronizeActiveStatusesSilentlyAsync();

    private void OnAuthenticationFileSystemWatcherRenamed(object sender, RenamedEventArgs renamedEventArguments) => _ = SynchronizeActiveStatusesSilentlyAsync();

    private async Task SynchronizeActiveStatusesSilentlyAsync()
    {
        try { await SynchronizeActiveStatusesAsync(); }
        catch { }
    }

    private static ProviderUsageSnapshot CreateProviderUsageSnapshot(CodexUsageSnapshot codexUsageSnapshot) => new()
    {
        ProviderKind = CliProviderKind.Codex,
        PlanType = codexUsageSnapshot.PlanType,
        EmailAddress = codexUsageSnapshot.EmailAddress,
        RawResponseText = codexUsageSnapshot.RawResponseText,
        FiveHour = CreateProviderUsageWindow(codexUsageSnapshot.PrimaryWindow),
        SevenDay = CreateProviderUsageWindow(codexUsageSnapshot.SecondaryWindow)
    };

    private static ProviderUsageWindow CreateProviderUsageWindow(CodexUsageWindow codexUsageWindow) => new()
    {
        UsedPercentage = codexUsageWindow.UsedPercentage,
        RemainingPercentage = codexUsageWindow.RemainingPercentage,
        ResetAfterSeconds = codexUsageWindow.ResetAfterSeconds,
        ResetAt = CreateDateTimeOffset(codexUsageWindow.ResetAtUnixSeconds)
    };

    private static string TryGetAccountIdentifier(CodexAuthenticationDocument codexAuthenticationDocument)
    {
        try { return codexAuthenticationDocument.GetEffectiveProfileIdentifier(); }
        catch { return ""; }
    }

    private static void UpdateEmailAddress(CodexAccount codexAccount, CodexUsageSnapshot codexUsageSnapshot)
    {
        if (!string.IsNullOrWhiteSpace(codexUsageSnapshot.EmailAddress)) codexAccount.CodexAuthenticationDocument.EmailAddress = codexUsageSnapshot.EmailAddress;
    }

    private static bool HasAuthenticationTokenInfoChanged(CodexAuthenticationDocument firstAuthenticationDocument, CodexAuthenticationDocument secondAuthenticationDocument)
        => !string.Equals(firstAuthenticationDocument.GetEffectiveIdentityToken(), secondAuthenticationDocument.GetEffectiveIdentityToken(), StringComparison.Ordinal) || !string.Equals(firstAuthenticationDocument.GetEffectiveAccessToken(), secondAuthenticationDocument.GetEffectiveAccessToken(), StringComparison.Ordinal) || !string.Equals(firstAuthenticationDocument.GetEffectiveRefreshToken(), secondAuthenticationDocument.GetEffectiveRefreshToken(), StringComparison.Ordinal) || !string.Equals(firstAuthenticationDocument.GetEffectiveAccountIdentifier(), secondAuthenticationDocument.GetEffectiveAccountIdentifier(), StringComparison.Ordinal) || !string.Equals(firstAuthenticationDocument.LastRefreshText, secondAuthenticationDocument.LastRefreshText, StringComparison.Ordinal) || !string.Equals(firstAuthenticationDocument.ExpirationText, secondAuthenticationDocument.ExpirationText, StringComparison.Ordinal);

    private static bool IsRefreshTokenFailureException(Exception exception)
    {
        if (exception is not CodexApiException codexApiException) return false;

        var statusCode = codexApiException.StatusCode;
        return statusCode is null || statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private static DateTimeOffset? CreateDateTimeOffset(long unixSeconds)
    {
        if (unixSeconds < 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch { return null; }
    }

    private static string BuildAccessTokenPreview(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return "";
        return accessToken.Length <= 18 ? accessToken : $"{accessToken[..8]}...{accessToken[^6..]}";
    }
}
