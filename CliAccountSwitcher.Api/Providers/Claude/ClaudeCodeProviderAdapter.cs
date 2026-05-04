using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliAccountSwitcher.Api.Infrastructure.Http;
using CliAccountSwitcher.Api.Providers;
using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Providers.Claude;

public sealed class ClaudeCodeProviderAdapter : IProviderAdapter, IDisposable
{
    private static readonly Uri s_usageAddress = new("https://api.anthropic.com/api/oauth/usage");
    private readonly HttpClient _httpClient;
    private readonly ClaudeCodeCliRunner _claudeCodeCliRunner;
    private readonly ClaudeCodeOAuthClient _claudeCodeOAuthClient;
    private readonly IProviderSnapshotStore _providerSnapshotStore;

    public ClaudeCodeProviderAdapter(IProviderSnapshotStore providerSnapshotStore)
    {
        _providerSnapshotStore = providerSnapshotStore;
        _httpClient = new HttpClient();
        _claudeCodeCliRunner = new ClaudeCodeCliRunner();
        _claudeCodeOAuthClient = new ClaudeCodeOAuthClient(_httpClient);
    }

    public CliProviderKind ProviderKind => CliProviderKind.ClaudeCode;

    public string DisplayName => "Claude Code";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        SupportsAuthenticationDocumentNormalization = false,
        SupportsModels = false,
        SupportsUsage = true,
        SupportsResponses = true,
        SupportsStreamingResponses = true,
        SupportsSavedAccounts = true,
        SupportsStoredAccountUsage = true,
        SupportsInteractiveLogin = true
    };

    public string? GetDefaultInputFilePath() => new ClaudeCodePaths().CredentialsFilePath;

    public async Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
    {
        var claudeCodeAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return CreateIdentityProfile(claudeCodeAccountState);
    }

    public Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default) => throw new NotSupportedException("Claude Code does not support raw authentication document normalization. Use save-current-account.");

    public async Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default)
    {
        _ = await _claudeCodeCliRunner.RunInteractiveLoginAsync(cancellationToken);
        return new ProviderLoginResult
        {
            ProviderKind = ProviderKind,
            OutputText = "",
            CompletionMessage = "Login may be complete. Now run save-current-account.",
            IsAuthenticationDocument = false,
            ShouldPromptSaveCurrentAccount = true
        };
    }

    public async Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storedAccountIdentifier))
        {
            var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
            liveAccountState = await RefreshLiveAccountIfNeededAsync(liveAccountState, cancellationToken);
            return await GetUsageSnapshotAsync(liveAccountState.CredentialDocument.AccessToken, liveAccountState.GlobalConfigDocument.EmailAddress, cancellationToken);
        }

        var storedPayloadContext = await LoadStoredPayloadContextAsync(_providerSnapshotStore, storedAccountIdentifier, cancellationToken);
        storedPayloadContext = await RefreshStoredAccountIfNeededAsync(storedPayloadContext, cancellationToken);
        return await GetUsageSnapshotAsync(storedPayloadContext.CredentialDocument.AccessToken, storedPayloadContext.Payload.EmailAddress, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Claude Code does not expose a models command through this provider.");

    public async Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model)) throw new InvalidOperationException("The response command requires --model for Claude Code.");
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The response command requires --text for Claude Code.");

        await _claudeCodeCliRunner.EnsureInstalledAsync(cancellationToken);

        var arguments = CreateResponseArguments(providerResponseRequest, false);
        var processResult = await _claudeCodeCliRunner.RunCapturedAsync(arguments, cancellationToken);
        if (processResult.ExitCode != 0) throw new ProviderActionRequiredException($"Claude Code command failed with exit code {processResult.ExitCode}: {processResult.ErrorText}");

        return new ProviderResponseResult
        {
            ProviderKind = ProviderKind,
            OutputText = processResult.OutputText,
            RawResponseText = processResult.OutputText
        };
    }

    public async IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model)) throw new InvalidOperationException("The stream-response command requires --model for Claude Code.");
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The stream-response command requires --text for Claude Code.");

        await _claudeCodeCliRunner.EnsureInstalledAsync(cancellationToken);

        var arguments = CreateResponseArguments(providerResponseRequest, true);
        await foreach (var outputLine in _claudeCodeCliRunner.StreamOutputLinesAsync(arguments, cancellationToken))
        {
            yield return CreateStreamEvent(outputLine);
        }

        yield return new ProviderResponseStreamEvent
        {
            EventName = "terminal",
            Data = "",
            PayloadNode = null,
            IsTerminal = true
        };
    }

    public async Task<IReadOnlyList<StoredProviderAccount>> ListStoredAccountsAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var liveIdentityProfile = await TryGetCurrentIdentityAsync(cancellationToken);

        return storedProviderAccounts
            .Select(storedProviderAccount => CloneStoredProviderAccount(storedProviderAccount, liveIdentityProfile is not null && IsSameClaudeAccount(storedProviderAccount, liveIdentityProfile) || storedProviderAccount.IsActive))
            .ToArray();
    }

    public async Task<StoredProviderAccount> SaveCurrentAccountAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return await SaveAccountCoreAsync(providerSnapshotStore, liveAccountState.CredentialsJson, liveAccountState.GlobalConfigJson, true, cancellationToken);
    }

    public async Task<StoredProviderAccount> SaveCurrentAccountWithoutActivationAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return await SaveAccountCoreAsync(providerSnapshotStore, liveAccountState.CredentialsJson, liveAccountState.GlobalConfigJson, false, cancellationToken);
    }

    public async Task<StoredProviderAccount> SaveAccountAsync(IProviderSnapshotStore providerSnapshotStore, string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default) => await SaveAccountCoreAsync(providerSnapshotStore, credentialsJson, globalConfigJson, true, cancellationToken);

    public async Task<StoredProviderAccount> SaveAccountWithoutActivationAsync(IProviderSnapshotStore providerSnapshotStore, string credentialsJson, string globalConfigJson, CancellationToken cancellationToken = default) => await SaveAccountCoreAsync(providerSnapshotStore, credentialsJson, globalConfigJson, false, cancellationToken);

    private async Task<StoredProviderAccount> SaveAccountCoreAsync(IProviderSnapshotStore providerSnapshotStore, string credentialsJson, string globalConfigJson, bool shouldSetActiveStoredAccountIdentifier, CancellationToken cancellationToken)
    {
        var liveAccountState = new ClaudeCodeAccountState
        {
            CredentialsJson = credentialsJson,
            GlobalConfigJson = globalConfigJson,
            CredentialDocument = ClaudeCodeCredentialDocument.Parse(credentialsJson),
            GlobalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(globalConfigJson)
        };
        ValidateAccountState(liveAccountState);

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var existingStoredProviderAccount = storedProviderAccounts.FirstOrDefault(storedProviderAccount => IsSameClaudeAccount(storedProviderAccount, liveAccountState.GlobalConfigDocument));
        var slotNumber = existingStoredProviderAccount?.SlotNumber ?? GetNextSlotNumber(storedProviderAccounts);
        var storedAccountIdentifier = existingStoredProviderAccount?.StoredAccountIdentifier ?? slotNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var storedProviderAccount = CreateStoredProviderAccount(liveAccountState, storedAccountIdentifier, slotNumber, shouldSetActiveStoredAccountIdentifier);
        var storedAccountPayload = CreateStoredAccountPayload(liveAccountState);
        var payloadJson = JsonSerializer.Serialize(storedAccountPayload, ProviderJsonSerializerOptions.Default);

        await providerSnapshotStore.SaveAsync(storedProviderAccount, payloadJson, cancellationToken);
        if (shouldSetActiveStoredAccountIdentifier) await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        return storedProviderAccount;
    }

    public async Task<StoredProviderAccount> ActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        var storedPayloadContext = await LoadStoredPayloadContextAsync(providerSnapshotStore, storedAccountIdentifier, cancellationToken);
        var claudeCodePaths = new ClaudeCodePaths();

        await WriteTextAtomicallyAsync(claudeCodePaths.CredentialsFilePath, storedPayloadContext.Payload.CredentialsJson, cancellationToken);
        await WriteTextAtomicallyAsync(claudeCodePaths.GlobalConfigFilePath, storedPayloadContext.Payload.GlobalConfigJson, cancellationToken);
        await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);

        var verifiedIdentityProfile = await TryGetCurrentIdentityAsync(cancellationToken);
        if (verifiedIdentityProfile is null || !IsSameClaudeAccount(storedPayloadContext.StoredProviderAccount, verifiedIdentityProfile))
        {
            throw new ProviderActionRequiredException("Claude Code account files were restored, but the restored identity could not be verified. Login again or re-save the account.");
        }

        return CloneStoredProviderAccount(storedPayloadContext.StoredProviderAccount, true);
    }

    public Task DeleteStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default) => providerSnapshotStore.DeleteAsync(ProviderKind, storedAccountIdentifier, cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<ProviderUsageSnapshot> GetUsageSnapshotAsync(string accessToken, string emailAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ProviderActionRequiredException("Claude Code login is required because the access token is missing.");

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, s_usageAddress);
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequestMessage.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        using var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await CodexHttpResponseValidator.ReadRequiredContentAsync(httpResponseMessage, cancellationToken);
        if (!httpResponseMessage.IsSuccessStatusCode) throw new ProviderActionRequiredException($"Claude Code usage request failed. Login again if the token is expired. Response: {responseText}");

        return ClaudeCodeUsageResponse.Parse(responseText, emailAddress);
    }

    private async Task<ClaudeCodeAccountState> RefreshLiveAccountIfNeededAsync(ClaudeCodeAccountState liveAccountState, CancellationToken cancellationToken)
    {
        if (!liveAccountState.CredentialDocument.IsAccessTokenExpiringSoon(DateTimeOffset.UtcNow)) return liveAccountState;

        var tokenRefreshResult = await _claudeCodeOAuthClient.RefreshTokenAsync(liveAccountState.CredentialDocument, cancellationToken);
        var updatedCredentialsJson = liveAccountState.CredentialDocument.CreateUpdatedCredentialsJson(tokenRefreshResult);
        var claudeCodePaths = new ClaudeCodePaths();
        await WriteTextAtomicallyAsync(claudeCodePaths.CredentialsFilePath, updatedCredentialsJson, cancellationToken);

        return new ClaudeCodeAccountState
        {
            CredentialsJson = updatedCredentialsJson,
            GlobalConfigJson = liveAccountState.GlobalConfigJson,
            CredentialDocument = ClaudeCodeCredentialDocument.Parse(updatedCredentialsJson),
            GlobalConfigDocument = liveAccountState.GlobalConfigDocument
        };
    }

    private async Task<StoredPayloadContext> RefreshStoredAccountIfNeededAsync(StoredPayloadContext storedPayloadContext, CancellationToken cancellationToken)
    {
        if (!storedPayloadContext.CredentialDocument.IsAccessTokenExpiringSoon(DateTimeOffset.UtcNow)) return storedPayloadContext;

        var tokenRefreshResult = await _claudeCodeOAuthClient.RefreshTokenAsync(storedPayloadContext.CredentialDocument, cancellationToken);
        storedPayloadContext.Payload.CredentialsJson = storedPayloadContext.CredentialDocument.CreateUpdatedCredentialsJson(tokenRefreshResult);
        storedPayloadContext.CredentialDocument = ClaudeCodeCredentialDocument.Parse(storedPayloadContext.Payload.CredentialsJson);
        storedPayloadContext.Payload.PlanType = storedPayloadContext.CredentialDocument.PlanType;
        storedPayloadContext.StoredProviderAccount.PlanType = storedPayloadContext.CredentialDocument.PlanType;
        storedPayloadContext.StoredProviderAccount.LastUpdated = DateTimeOffset.UtcNow;

        var payloadJson = JsonSerializer.Serialize(storedPayloadContext.Payload, ProviderJsonSerializerOptions.Default);
        await _providerSnapshotStore.SaveAsync(storedPayloadContext.StoredProviderAccount, payloadJson, cancellationToken);
        return storedPayloadContext;
    }

    private async Task<ClaudeCodeAccountState> ReadLiveAccountStateAsync(CancellationToken cancellationToken)
    {
        var claudeCodePaths = new ClaudeCodePaths();
        if (!File.Exists(claudeCodePaths.CredentialsFilePath)) throw new ProviderActionRequiredException("Claude Code login is required.");
        if (!File.Exists(claudeCodePaths.GlobalConfigFilePath)) throw new ProviderActionRequiredException("Claude Code login is required.");

        var credentialsJson = await File.ReadAllTextAsync(claudeCodePaths.CredentialsFilePath, cancellationToken);
        var globalConfigJson = await File.ReadAllTextAsync(claudeCodePaths.GlobalConfigFilePath, cancellationToken);
        var credentialDocument = ClaudeCodeCredentialDocument.Parse(credentialsJson);
        var globalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(globalConfigJson);

        if (string.IsNullOrWhiteSpace(credentialDocument.AccessToken) || string.IsNullOrWhiteSpace(globalConfigDocument.EmailAddress))
        {
            throw new ProviderActionRequiredException("Claude Code login is required.");
        }

        return new ClaudeCodeAccountState
        {
            CredentialsJson = credentialsJson,
            GlobalConfigJson = globalConfigJson,
            CredentialDocument = credentialDocument,
            GlobalConfigDocument = globalConfigDocument
        };
    }

    private static void ValidateAccountState(ClaudeCodeAccountState claudeCodeAccountState)
    {
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.CredentialDocument.AccessToken)) throw new ProviderActionRequiredException("Claude Code credentials are missing claudeAiOauth.accessToken.");
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.CredentialDocument.RefreshToken)) throw new ProviderActionRequiredException("Claude Code credentials are missing claudeAiOauth.refreshToken.");
        if (claudeCodeAccountState.CredentialDocument.ExpiresAt <= 0) throw new ProviderActionRequiredException("Claude Code credentials are missing claudeAiOauth.expiresAt.");
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.EmailAddress)) throw new ProviderActionRequiredException("Claude Code global config is missing oauthAccount.emailAddress.");
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.AccountIdentifier)) throw new ProviderActionRequiredException("Claude Code global config is missing oauthAccount.accountUuid.");
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.OrganizationIdentifier)) throw new ProviderActionRequiredException("Claude Code global config is missing oauthAccount.organizationUuid.");
        if (string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.OrganizationName)) throw new ProviderActionRequiredException("Claude Code global config is missing oauthAccount.organizationName.");
    }

    private async Task<StoredPayloadContext> LoadStoredPayloadContextAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var payloadJson = await providerSnapshotStore.GetPayloadJsonAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ProviderActionRequiredException($"The stored Claude Code account slot was not found: {storedAccountIdentifier}");

        var storedAccountPayload = JsonSerializer.Deserialize<ClaudeCodeStoredAccountPayload>(payloadJson, ProviderJsonSerializerOptions.Default);
        if (storedAccountPayload is null || string.IsNullOrWhiteSpace(storedAccountPayload.CredentialsJson) || string.IsNullOrWhiteSpace(storedAccountPayload.GlobalConfigJson))
        {
            throw new ProviderActionRequiredException($"The stored Claude Code account slot is invalid: {storedAccountIdentifier}");
        }

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var storedProviderAccount = storedProviderAccounts.FirstOrDefault(candidateAccount => string.Equals(candidateAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
        if (storedProviderAccount is null) throw new ProviderActionRequiredException($"The stored Claude Code account slot was not found: {storedAccountIdentifier}");

        return new StoredPayloadContext
        {
            Payload = storedAccountPayload,
            CredentialDocument = ClaudeCodeCredentialDocument.Parse(storedAccountPayload.CredentialsJson),
            GlobalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(storedAccountPayload.GlobalConfigJson),
            StoredProviderAccount = storedProviderAccount
        };
    }

    private async Task<ProviderIdentityProfile?> TryGetCurrentIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetCurrentIdentityAsync(cancellationToken);
        }
        catch { return null; }
    }

    private static ProviderIdentityProfile CreateIdentityProfile(ClaudeCodeAccountState claudeCodeAccountState)
        => new()
        {
            ProviderKind = CliProviderKind.ClaudeCode,
            EmailAddress = claudeCodeAccountState.GlobalConfigDocument.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.DisplayName) ? claudeCodeAccountState.GlobalConfigDocument.EmailAddress : claudeCodeAccountState.GlobalConfigDocument.DisplayName,
            AccountIdentifier = claudeCodeAccountState.GlobalConfigDocument.AccountIdentifier,
            OrganizationIdentifier = claudeCodeAccountState.GlobalConfigDocument.OrganizationIdentifier,
            OrganizationName = claudeCodeAccountState.GlobalConfigDocument.OrganizationName,
            PlanType = claudeCodeAccountState.CredentialDocument.PlanType,
            AccessTokenPreview = BuildAccessTokenPreview(claudeCodeAccountState.CredentialDocument.AccessToken),
            ExpirationText = claudeCodeAccountState.CredentialDocument.GetExpirationText(),
            IsLoggedIn = !string.IsNullOrWhiteSpace(claudeCodeAccountState.CredentialDocument.AccessToken) && !string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.EmailAddress)
        };

    private static StoredProviderAccount CreateStoredProviderAccount(ClaudeCodeAccountState claudeCodeAccountState, string storedAccountIdentifier, int slotNumber, bool isActive)
        => new()
        {
            ProviderKind = CliProviderKind.ClaudeCode,
            StoredAccountIdentifier = storedAccountIdentifier,
            SlotNumber = slotNumber,
            EmailAddress = claudeCodeAccountState.GlobalConfigDocument.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(claudeCodeAccountState.GlobalConfigDocument.DisplayName) ? claudeCodeAccountState.GlobalConfigDocument.EmailAddress : claudeCodeAccountState.GlobalConfigDocument.DisplayName,
            AccountIdentifier = claudeCodeAccountState.GlobalConfigDocument.AccountIdentifier,
            OrganizationIdentifier = claudeCodeAccountState.GlobalConfigDocument.OrganizationIdentifier,
            OrganizationName = claudeCodeAccountState.GlobalConfigDocument.OrganizationName,
            PlanType = claudeCodeAccountState.CredentialDocument.PlanType,
            IsActive = isActive,
            IsTokenExpired = false,
            LastUpdated = DateTimeOffset.UtcNow
        };

    private static ClaudeCodeStoredAccountPayload CreateStoredAccountPayload(ClaudeCodeAccountState liveAccountState)
        => new()
        {
            CredentialsJson = liveAccountState.CredentialsJson,
            GlobalConfigJson = liveAccountState.GlobalConfigJson,
            EmailAddress = liveAccountState.GlobalConfigDocument.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(liveAccountState.GlobalConfigDocument.DisplayName) ? liveAccountState.GlobalConfigDocument.EmailAddress : liveAccountState.GlobalConfigDocument.DisplayName,
            AccountIdentifier = liveAccountState.GlobalConfigDocument.AccountIdentifier,
            OrganizationIdentifier = liveAccountState.GlobalConfigDocument.OrganizationIdentifier,
            OrganizationName = liveAccountState.GlobalConfigDocument.OrganizationName,
            PlanType = liveAccountState.CredentialDocument.PlanType
        };

    private static StoredProviderAccount CloneStoredProviderAccount(StoredProviderAccount storedProviderAccount, bool isActive)
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
            IsActive = isActive,
            IsTokenExpired = storedProviderAccount.IsTokenExpired,
            LastUpdated = storedProviderAccount.LastUpdated
        };

    private static bool IsSameClaudeAccount(StoredProviderAccount storedProviderAccount, ClaudeCodeGlobalConfigDocument globalConfigDocument)
        => string.Equals(storedProviderAccount.EmailAddress, globalConfigDocument.EmailAddress, StringComparison.OrdinalIgnoreCase)
           && string.Equals(storedProviderAccount.OrganizationIdentifier, globalConfigDocument.OrganizationIdentifier, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameClaudeAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile identityProfile)
        => string.Equals(storedProviderAccount.EmailAddress, identityProfile.EmailAddress, StringComparison.OrdinalIgnoreCase)
           && string.Equals(storedProviderAccount.OrganizationIdentifier, identityProfile.OrganizationIdentifier, StringComparison.OrdinalIgnoreCase);

    private static int GetNextSlotNumber(IReadOnlyList<StoredProviderAccount> storedProviderAccounts) => storedProviderAccounts.Count == 0 ? 1 : storedProviderAccounts.Max(storedProviderAccount => storedProviderAccount.SlotNumber) + 1;

    private static IReadOnlyList<string> CreateResponseArguments(ProviderResponseRequest providerResponseRequest, bool useStreamingOutput)
    {
        var arguments = new List<string>
        {
            "-p",
            providerResponseRequest.Text,
            "--model",
            providerResponseRequest.Model
        };

        if (!string.IsNullOrWhiteSpace(providerResponseRequest.Instructions))
        {
            arguments.Add("--append-system-prompt");
            arguments.Add(providerResponseRequest.Instructions);
        }

        if (useStreamingOutput)
        {
            arguments.Add("--output-format");
            arguments.Add("stream-json");
        }

        return arguments;
    }

    private static ProviderResponseStreamEvent CreateStreamEvent(string outputLine)
    {
        try
        {
            var payloadNode = JsonNode.Parse(outputLine);
            var eventName = payloadNode is JsonObject payloadObject ? payloadObject["type"]?.ToString() ?? "message" : "message";
            return new ProviderResponseStreamEvent
            {
                EventName = eventName,
                Data = outputLine,
                PayloadNode = payloadNode,
                IsTerminal = false
            };
        }
        catch
        {
            return new ProviderResponseStreamEvent
            {
                EventName = "message",
                Data = outputLine,
                PayloadNode = null,
                IsTerminal = false
            };
        }
    }

    private static string BuildAccessTokenPreview(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return "(missing)";
        return accessToken.Length <= 18 ? accessToken : $"{accessToken[..8]}...{accessToken[^6..]}";
    }

    private static async Task WriteTextAtomicallyAsync(string filePath, string fileText, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);

        var temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryFilePath, fileText, Encoding.UTF8, cancellationToken);
        File.Move(temporaryFilePath, filePath, true);
    }

    private sealed class ClaudeCodeAccountState
    {
        public string CredentialsJson { get; set; } = "";

        public string GlobalConfigJson { get; set; } = "";

        public ClaudeCodeCredentialDocument CredentialDocument { get; set; } = new();

        public ClaudeCodeGlobalConfigDocument GlobalConfigDocument { get; set; } = new();
    }

    private sealed class StoredPayloadContext
    {
        public ClaudeCodeStoredAccountPayload Payload { get; set; } = new();

        public ClaudeCodeCredentialDocument CredentialDocument { get; set; } = new();

        public ClaudeCodeGlobalConfigDocument GlobalConfigDocument { get; set; } = new();

        public StoredProviderAccount StoredProviderAccount { get; set; } = new();
    }
}
