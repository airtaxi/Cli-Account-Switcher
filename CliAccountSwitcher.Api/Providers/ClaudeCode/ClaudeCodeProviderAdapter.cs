using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Authentication;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Infrastructure;
using CliAccountSwitcher.Api.Providers.ClaudeCode.Models;
using CliAccountSwitcher.Api.Providers.Serialization;

namespace CliAccountSwitcher.Api.Providers.ClaudeCode;

public sealed class ClaudeCodeProviderAdapter : ProviderAdapterBase<ClaudeCodeAccountState, ClaudeCodeStoredAccountPayload>, IDisposable
{
    private static readonly Uri s_usageAddress = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly Encoding s_utf8NoByteOrderMark = new UTF8Encoding(false);
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

    public override CliProviderKind ProviderKind => CliProviderKind.ClaudeCode;

    public override string DisplayName => "Claude Code";

    public override ProviderCapabilities Capabilities { get; } = new()
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

    public override string? GetDefaultInputFilePath() => new ClaudeCodePaths().CredentialsFilePath;

    public override async Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
    {
        var claudeCodeAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return CreateIdentityProfile(claudeCodeAccountState);
    }

    public override Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default) => throw new NotSupportedException("Claude Code does not support raw authentication document normalization. Use save-current-account.");

    public override async Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default)
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

    public override async Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storedAccountIdentifier))
        {
            var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
            liveAccountState = await RefreshLiveAccountAsync(liveAccountState, true, cancellationToken);
            if (ShouldSkipUsageSnapshot(liveAccountState.CredentialDocument)) return CreateEmptyUsageSnapshot(liveAccountState.GlobalConfigDocument.EmailAddress);

            try
            {
                return await GetUsageSnapshotAsync(liveAccountState.CredentialDocument.AccessToken, liveAccountState.GlobalConfigDocument.EmailAddress, cancellationToken);
            }
            catch (ClaudeCodeUsageUnauthorizedException)
            {
                liveAccountState = await RefreshLiveAccountAsync(liveAccountState, true, cancellationToken);
                if (ShouldSkipUsageSnapshot(liveAccountState.CredentialDocument)) return CreateEmptyUsageSnapshot(liveAccountState.GlobalConfigDocument.EmailAddress);
                return await GetUsageSnapshotAfterRefreshAsync(liveAccountState.CredentialDocument.AccessToken, liveAccountState.GlobalConfigDocument.EmailAddress, cancellationToken);
            }
        }

        var storedPayloadContext = await LoadStoredPayloadContextAsync(_providerSnapshotStore, storedAccountIdentifier, cancellationToken);
        storedPayloadContext = await RefreshStoredAccountAsync(_providerSnapshotStore, storedPayloadContext, true, cancellationToken);
        if (ShouldSkipUsageSnapshot(storedPayloadContext.CredentialDocument)) return CreateEmptyUsageSnapshot(storedPayloadContext.Payload.EmailAddress);

        try
        {
            return await GetUsageSnapshotAsync(storedPayloadContext.CredentialDocument.AccessToken, storedPayloadContext.Payload.EmailAddress, cancellationToken);
        }
        catch (ClaudeCodeUsageUnauthorizedException)
        {
            storedPayloadContext = await RefreshStoredAccountAsync(_providerSnapshotStore, storedPayloadContext, true, cancellationToken);
            if (ShouldSkipUsageSnapshot(storedPayloadContext.CredentialDocument)) return CreateEmptyUsageSnapshot(storedPayloadContext.Payload.EmailAddress);
            return await GetUsageSnapshotAfterRefreshAsync(storedPayloadContext.CredentialDocument.AccessToken, storedPayloadContext.Payload.EmailAddress, cancellationToken);
        }
    }

    public override Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Claude Code does not expose a models command through this provider.");

    public override async Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default)
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

    public override async IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    public void Dispose() => _httpClient.Dispose();

    protected override async Task<ClaudeCodeAccountState> ReadLiveAccountStateAsync(CancellationToken cancellationToken)
    {
        var claudeCodePaths = new ClaudeCodePaths();
        if (!File.Exists(claudeCodePaths.CredentialsFilePath)) throw new ProviderActionRequiredException("Claude Code login is required.");
        if (!File.Exists(claudeCodePaths.GlobalConfigFilePath)) throw new ProviderActionRequiredException("Claude Code login is required.");

        return CreateClaudeCodeAccountState(
            await File.ReadAllTextAsync(claudeCodePaths.CredentialsFilePath, cancellationToken),
            await File.ReadAllTextAsync(claudeCodePaths.GlobalConfigFilePath, cancellationToken),
            true);
    }

    protected override ClaudeCodeAccountState CreateLiveAccountState(ProviderAccountDocumentSet providerAccountDocumentSet)
    {
        if (string.IsNullOrWhiteSpace(providerAccountDocumentSet.CredentialsDocumentText)) throw new ProviderActionRequiredException("The Claude Code credentials document is required.");
        if (string.IsNullOrWhiteSpace(providerAccountDocumentSet.GlobalConfigDocumentText)) throw new ProviderActionRequiredException("The Claude Code global config document is required.");
        return CreateClaudeCodeAccountState(providerAccountDocumentSet.CredentialsDocumentText, providerAccountDocumentSet.GlobalConfigDocumentText, true);
    }

    protected override ClaudeCodeAccountState CreateLiveAccountState(ClaudeCodeStoredAccountPayload storedAccountPayload)
    {
        if (storedAccountPayload is null || string.IsNullOrWhiteSpace(storedAccountPayload.CredentialsJson) || string.IsNullOrWhiteSpace(storedAccountPayload.GlobalConfigJson))
        {
            throw new ProviderActionRequiredException("The stored Claude Code account payload is invalid.");
        }

        return CreateClaudeCodeAccountState(storedAccountPayload.CredentialsJson, storedAccountPayload.GlobalConfigJson, true);
    }

    protected override ClaudeCodeStoredAccountPayload CreateStoredAccountPayload(ClaudeCodeAccountState liveAccountState)
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

    protected override string SerializeStoredAccountPayload(ClaudeCodeStoredAccountPayload storedAccountPayload) => JsonSerializer.Serialize(storedAccountPayload, ProviderJsonSerializerOptions.Default);

    protected override ClaudeCodeStoredAccountPayload DeserializeStoredAccountPayload(string payloadJson, string storedAccountIdentifier)
    {
        var storedAccountPayload = JsonSerializer.Deserialize<ClaudeCodeStoredAccountPayload>(payloadJson, ProviderJsonSerializerOptions.Default);
        if (storedAccountPayload is null || string.IsNullOrWhiteSpace(storedAccountPayload.CredentialsJson) || string.IsNullOrWhiteSpace(storedAccountPayload.GlobalConfigJson))
        {
            throw new ProviderActionRequiredException($"The stored Claude Code account slot is invalid: {storedAccountIdentifier}");
        }

        return storedAccountPayload;
    }

    protected override StoredProviderAccount CreateStoredProviderAccount(ClaudeCodeAccountState liveAccountState, string storedAccountIdentifier, int slotNumber, bool isActive)
        => new()
        {
            ProviderKind = CliProviderKind.ClaudeCode,
            StoredAccountIdentifier = storedAccountIdentifier,
            SlotNumber = slotNumber,
            EmailAddress = liveAccountState.GlobalConfigDocument.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(liveAccountState.GlobalConfigDocument.DisplayName) ? liveAccountState.GlobalConfigDocument.EmailAddress : liveAccountState.GlobalConfigDocument.DisplayName,
            AccountIdentifier = liveAccountState.GlobalConfigDocument.AccountIdentifier,
            OrganizationIdentifier = liveAccountState.GlobalConfigDocument.OrganizationIdentifier,
            OrganizationName = liveAccountState.GlobalConfigDocument.OrganizationName,
            PlanType = liveAccountState.CredentialDocument.PlanType,
            IsActive = isActive,
            IsTokenExpired = false,
            LastUpdated = DateTimeOffset.UtcNow,
            LastProviderUsageSnapshot = new ProviderUsageSnapshot { ProviderKind = CliProviderKind.ClaudeCode }
        };

    protected override bool IsSameAccount(StoredProviderAccount storedProviderAccount, ClaudeCodeAccountState liveAccountState) => IsSameClaudeCodeAccount(storedProviderAccount, liveAccountState.GlobalConfigDocument);

    protected override bool IsSameAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile providerIdentityProfile) => IsSameClaudeCodeAccount(storedProviderAccount, providerIdentityProfile);

    protected override async Task WriteLiveAccountStateAsync(ClaudeCodeAccountState liveAccountState, CancellationToken cancellationToken)
    {
        var claudeCodePaths = new ClaudeCodePaths();
        await WriteTextAtomicallyAsync(claudeCodePaths.CredentialsFilePath, liveAccountState.CredentialsJson, cancellationToken);
        await WriteTextAtomicallyAsync(claudeCodePaths.GlobalConfigFilePath, liveAccountState.GlobalConfigJson, cancellationToken);
    }

    protected override async Task BeforeActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken) => await BackupCurrentLiveAccountIfManagedAsync(providerSnapshotStore, cancellationToken);

    protected override async Task<ClaudeCodeStoredAccountPayload> PrepareStoredAccountPayloadForActivationAsync(IProviderSnapshotStore providerSnapshotStore, StoredProviderAccount storedProviderAccount, ClaudeCodeStoredAccountPayload storedAccountPayload, CancellationToken cancellationToken)
    {
        var storedPayloadContext = CreateStoredPayloadContext(storedProviderAccount, storedAccountPayload);
        storedPayloadContext = await RefreshStoredAccountAsync(providerSnapshotStore, storedPayloadContext, false, cancellationToken);
        return storedPayloadContext.Payload;
    }

    protected override async Task WriteLiveAccountStateForActivationAsync(IProviderSnapshotStore providerSnapshotStore, StoredProviderAccount storedProviderAccount, ClaudeCodeAccountState liveAccountState, CancellationToken cancellationToken)
    {
        var claudeCodePaths = new ClaudeCodePaths();
        var originalCredentialsJson = await TryReadAllTextAsync(claudeCodePaths.CredentialsFilePath, cancellationToken);
        var originalGlobalConfigJson = await TryReadAllTextAsync(claudeCodePaths.GlobalConfigFilePath, cancellationToken);
        var hasWrittenCredentials = false;
        var hasWrittenGlobalConfig = false;

        try
        {
            await WriteTextAtomicallyAsync(claudeCodePaths.CredentialsFilePath, liveAccountState.CredentialsJson, cancellationToken);
            hasWrittenCredentials = true;

            var currentGlobalConfigJson = originalGlobalConfigJson ?? "{}";
            var updatedGlobalConfigJson = CreateGlobalConfigJsonWithTargetOauthAccount(currentGlobalConfigJson, liveAccountState.GlobalConfigJson);
            await WriteTextAtomicallyAsync(claudeCodePaths.GlobalConfigFilePath, updatedGlobalConfigJson, cancellationToken);
            hasWrittenGlobalConfig = true;

            var verifiedIdentityProfile = await TryGetCurrentIdentityForActivationAsync(cancellationToken);
            if (verifiedIdentityProfile is null || !IsSameClaudeCodeAccount(storedProviderAccount, verifiedIdentityProfile))
            {
                throw new ProviderActionRequiredException("Claude Code account files were restored, but the restored identity could not be verified. Login again or re-save the account.");
            }
        }
        catch
        {
            try
            {
                await RestoreLiveClaudeCodeFilesAsync(claudeCodePaths, originalCredentialsJson, originalGlobalConfigJson, hasWrittenCredentials, hasWrittenGlobalConfig, CancellationToken.None);
            }
            catch { }

            throw;
        }
    }

    private async Task<ProviderUsageSnapshot> GetUsageSnapshotAsync(string accessToken, string emailAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ProviderActionRequiredException("Claude Code login is required because the access token is missing.");

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, s_usageAddress);
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequestMessage.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        using var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        if (httpResponseMessage.StatusCode == HttpStatusCode.Unauthorized) throw new ClaudeCodeUsageUnauthorizedException(responseText);
        if (!httpResponseMessage.IsSuccessStatusCode) throw new ProviderActionRequiredException($"Claude Code usage request failed. Login again if the token is expired. Response: {responseText}");
        if (string.IsNullOrWhiteSpace(responseText)) throw new ProviderActionRequiredException("Claude Code usage request failed because the response body is empty.");

        return ClaudeCodeUsageResponse.Parse(responseText, emailAddress);
    }

    private async Task<ProviderUsageSnapshot> GetUsageSnapshotAfterRefreshAsync(string accessToken, string emailAddress, CancellationToken cancellationToken)
    {
        try { return await GetUsageSnapshotAsync(accessToken, emailAddress, cancellationToken); }
        catch (ClaudeCodeUsageUnauthorizedException exception)
        {
            throw new ProviderAuthenticationExpiredException("Claude Code usage request failed after refreshing the token. Login again or re-save the account.", exception);
        }
    }

    private static bool ShouldSkipUsageSnapshot(ClaudeCodeCredentialDocument credentialDocument)
        => credentialDocument.Scopes.Count > 0 && !credentialDocument.HasClaudeAiUsageScopes();

    private static ProviderUsageSnapshot CreateEmptyUsageSnapshot(string emailAddress)
        => new()
        {
            ProviderKind = CliProviderKind.ClaudeCode,
            EmailAddress = emailAddress
        };

    private async Task<ClaudeCodeAccountState> RefreshLiveAccountAsync(ClaudeCodeAccountState liveAccountState, bool shouldForceRefresh, CancellationToken cancellationToken)
    {
        if (!shouldForceRefresh && !liveAccountState.CredentialDocument.IsAccessTokenExpiringSoon(DateTimeOffset.UtcNow)) return liveAccountState;

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

    private async Task<StoredPayloadContext> RefreshStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, StoredPayloadContext storedPayloadContext, bool shouldForceRefresh, CancellationToken cancellationToken)
    {
        if (!shouldForceRefresh && !storedPayloadContext.CredentialDocument.IsAccessTokenExpiringSoon(DateTimeOffset.UtcNow)) return storedPayloadContext;

        var tokenRefreshResult = await _claudeCodeOAuthClient.RefreshTokenAsync(storedPayloadContext.CredentialDocument, cancellationToken);
        storedPayloadContext.Payload.CredentialsJson = storedPayloadContext.CredentialDocument.CreateUpdatedCredentialsJson(tokenRefreshResult);
        storedPayloadContext.CredentialDocument = ClaudeCodeCredentialDocument.Parse(storedPayloadContext.Payload.CredentialsJson);
        storedPayloadContext.Payload.PlanType = storedPayloadContext.CredentialDocument.PlanType;
        storedPayloadContext.StoredProviderAccount.PlanType = storedPayloadContext.CredentialDocument.PlanType;
        storedPayloadContext.StoredProviderAccount.LastUpdated = DateTimeOffset.UtcNow;

        var payloadJson = JsonSerializer.Serialize(storedPayloadContext.Payload, ProviderJsonSerializerOptions.Default);
        await providerSnapshotStore.SaveAsync(storedPayloadContext.StoredProviderAccount, payloadJson, cancellationToken);
        return storedPayloadContext;
    }

    private static ClaudeCodeAccountState CreateClaudeCodeAccountState(string credentialsJson, string globalConfigJson, bool shouldValidateAccountState)
    {
        var claudeCodeAccountState = new ClaudeCodeAccountState
        {
            CredentialsJson = credentialsJson,
            GlobalConfigJson = globalConfigJson,
            CredentialDocument = ClaudeCodeCredentialDocument.Parse(credentialsJson),
            GlobalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(globalConfigJson)
        };

        if (shouldValidateAccountState) ValidateAccountState(claudeCodeAccountState);
        return claudeCodeAccountState;
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

    private static StoredPayloadContext CreateStoredPayloadContext(StoredProviderAccount storedProviderAccount, ClaudeCodeStoredAccountPayload storedAccountPayload)
        => new()
        {
            Payload = storedAccountPayload,
            CredentialDocument = ClaudeCodeCredentialDocument.Parse(storedAccountPayload.CredentialsJson),
            GlobalConfigDocument = ClaudeCodeGlobalConfigDocument.Parse(storedAccountPayload.GlobalConfigJson),
            StoredProviderAccount = storedProviderAccount
        };

    private async Task<StoredPayloadContext> LoadStoredPayloadContextAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var payloadJson = await providerSnapshotStore.GetPayloadJsonAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ProviderActionRequiredException($"The stored Claude Code account slot was not found: {storedAccountIdentifier}");

        var storedAccountPayload = DeserializeStoredAccountPayload(payloadJson, storedAccountIdentifier);

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var storedProviderAccount = storedProviderAccounts.FirstOrDefault(candidateAccount => string.Equals(candidateAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
        if (storedProviderAccount is null) throw new ProviderActionRequiredException($"The stored Claude Code account slot was not found: {storedAccountIdentifier}");

        return CreateStoredPayloadContext(storedProviderAccount, storedAccountPayload);
    }

    private async Task BackupCurrentLiveAccountIfManagedAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken)
    {
        ClaudeCodeAccountState liveAccountState;
        try { liveAccountState = await ReadLiveAccountStateAsync(cancellationToken); }
        catch { return; }

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var storedProviderAccount = storedProviderAccounts.FirstOrDefault(candidateAccount => IsSameClaudeCodeAccount(candidateAccount, liveAccountState.GlobalConfigDocument));
        if (storedProviderAccount is null) return;

        await UpdateStoredAccountFromCurrentLiveAccountAsync(providerSnapshotStore, storedProviderAccount.StoredAccountIdentifier, cancellationToken);
    }

    private async Task<ProviderIdentityProfile?> TryGetCurrentIdentityForActivationAsync(CancellationToken cancellationToken)
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

    private static bool IsSameClaudeCodeAccount(StoredProviderAccount storedProviderAccount, ClaudeCodeGlobalConfigDocument globalConfigDocument)
        => string.Equals(storedProviderAccount.EmailAddress, globalConfigDocument.EmailAddress, StringComparison.OrdinalIgnoreCase)
           && string.Equals(storedProviderAccount.OrganizationIdentifier, globalConfigDocument.OrganizationIdentifier, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameClaudeCodeAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile identityProfile)
        => string.Equals(storedProviderAccount.EmailAddress, identityProfile.EmailAddress, StringComparison.OrdinalIgnoreCase)
           && string.Equals(storedProviderAccount.OrganizationIdentifier, identityProfile.OrganizationIdentifier, StringComparison.OrdinalIgnoreCase);

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

    private static string CreateGlobalConfigJsonWithTargetOauthAccount(string currentGlobalConfigJson, string targetGlobalConfigJson)
    {
        var currentGlobalConfigObject = ParseJsonObject(currentGlobalConfigJson, "current global config");
        var targetGlobalConfigObject = ParseJsonObject(targetGlobalConfigJson, "stored global config");
        if (!targetGlobalConfigObject.TryGetPropertyValue("oauthAccount", out var targetOauthAccountNode) || targetOauthAccountNode is not JsonObject)
        {
            throw new ProviderActionRequiredException("The stored Claude Code global config is missing oauthAccount.");
        }

        currentGlobalConfigObject["oauthAccount"] = targetOauthAccountNode.DeepClone();
        return currentGlobalConfigObject.ToJsonString(ProviderJsonSerializerOptions.Default);
    }

    private static JsonObject ParseJsonObject(string jsonText, string documentName)
    {
        if (string.IsNullOrWhiteSpace(jsonText)) return [];

        var rootNode = JsonNode.Parse(jsonText);
        if (rootNode is JsonObject rootObject) return rootObject;
        throw new InvalidDataException($"The Claude Code {documentName} document must be a JSON object.");
    }

    private static async Task<string?> TryReadAllTextAsync(string filePath, CancellationToken cancellationToken)
    {
        try { return File.Exists(filePath) ? await File.ReadAllTextAsync(filePath, cancellationToken) : null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (FileNotFoundException) { return null; }
    }

    private static async Task RestoreLiveClaudeCodeFilesAsync(ClaudeCodePaths claudeCodePaths, string? originalCredentialsJson, string? originalGlobalConfigJson, bool hasWrittenCredentials, bool hasWrittenGlobalConfig, CancellationToken cancellationToken)
    {
        if (hasWrittenCredentials) await RestoreTextFileAsync(claudeCodePaths.CredentialsFilePath, originalCredentialsJson, cancellationToken);
        if (hasWrittenGlobalConfig) await RestoreTextFileAsync(claudeCodePaths.GlobalConfigFilePath, originalGlobalConfigJson, cancellationToken);
    }

    private static async Task RestoreTextFileAsync(string filePath, string? originalFileText, CancellationToken cancellationToken)
    {
        if (originalFileText is null)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return;
        }

        await WriteTextAtomicallyAsync(filePath, originalFileText, cancellationToken);
    }

    private static async Task WriteTextAtomicallyAsync(string filePath, string fileText, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);

        var temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryFilePath, fileText, s_utf8NoByteOrderMark, cancellationToken);
        File.Move(temporaryFilePath, filePath, true);
    }

    private sealed class StoredPayloadContext
    {
        public ClaudeCodeStoredAccountPayload Payload { get; set; } = new();

        public ClaudeCodeCredentialDocument CredentialDocument { get; set; } = new();

        public ClaudeCodeGlobalConfigDocument GlobalConfigDocument { get; set; } = new();

        public StoredProviderAccount StoredProviderAccount { get; set; } = new();
    }

    private sealed class ClaudeCodeUsageUnauthorizedException(string responseText) : Exception(responseText)
    {
    }
}
