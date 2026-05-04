using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CliAccountSwitcher.Api.Authentication;
using CliAccountSwitcher.Api.Infrastructure;
using CliAccountSwitcher.Api.Infrastructure.Http;
using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.Authentication;
using CliAccountSwitcher.Api.Models.Responses;
using CliAccountSwitcher.Api.Providers;
using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Providers.Codex;

public sealed class CodexProviderAdapter : IProviderAdapter, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CodexApiClientOptions _codexApiClientOptions;
    private readonly CodexAuthenticationDocumentSerializer _codexAuthenticationDocumentSerializer;
    private readonly CodexOAuthClient _codexOAuthClient;
    private readonly CodexUsageClient _codexUsageClient;
    private readonly CodexModelsClient _codexModelsClient;
    private readonly CodexResponsesClient _codexResponsesClient;
    private readonly IProviderSnapshotStore? _providerSnapshotStore;

    public CodexProviderAdapter(IProviderSnapshotStore? providerSnapshotStore = null)
    {
        _providerSnapshotStore = providerSnapshotStore;
        _httpClient = CodexHttpClientFactory.CreateDefault();
        _codexApiClientOptions = new CodexApiClientOptions();
        _codexAuthenticationDocumentSerializer = new CodexAuthenticationDocumentSerializer();

        var codexClientMetadataProvider = new CodexClientMetadataProvider(_codexApiClientOptions);
        var codexRequestMessageFactory = new CodexRequestMessageFactory(_codexApiClientOptions, codexClientMetadataProvider);

        _codexOAuthClient = new CodexOAuthClient(_httpClient, _codexApiClientOptions, codexRequestMessageFactory);
        _codexUsageClient = new CodexUsageClient(_httpClient, codexRequestMessageFactory);
        _codexModelsClient = new CodexModelsClient(_httpClient, codexRequestMessageFactory);
        _codexResponsesClient = new CodexResponsesClient(_httpClient, codexRequestMessageFactory);
    }

    public CliProviderKind ProviderKind => CliProviderKind.Codex;

    public string DisplayName => "Codex";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        SupportsAuthenticationDocumentNormalization = true,
        SupportsModels = true,
        SupportsUsage = true,
        SupportsResponses = true,
        SupportsStreamingResponses = true,
        SupportsSavedAccounts = true,
        SupportsStoredAccountUsage = true,
        SupportsInteractiveLogin = true
    };

    public string? InputFilePathOverride { get; set; }

    public string? GetDefaultInputFilePath() => BuildDefaultAuthenticationFilePath();

    public async Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(cancellationToken);
        return CreateIdentityProfile(codexAuthenticationDocument);
    }

    public Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default) => Task.FromResult(CodexAuthenticationDocumentSerializer.Normalize(authenticationDocumentText));

    public async Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default)
    {
        await using var codexOAuthSession = _codexOAuthClient.CreateSession();

        Console.WriteLine("Open the following URL in a browser and complete the authorization flow:");
        Console.WriteLine(codexOAuthSession.AuthorizationAddress);
        if (TryOpenBrowser(codexOAuthSession.AuthorizationAddress)) Console.WriteLine("The system browser has been opened automatically.");
        else Console.WriteLine("The system browser could not be opened automatically. Open the URL manually.");
        Console.WriteLine($"Listening for the OAuth callback on: {codexOAuthSession.RedirectAddress}");
        Console.WriteLine();
        Console.WriteLine("Waiting for the loopback callback...");

        var codexOAuthCallbackPayload = await codexOAuthSession.WaitForCallbackAsync(cancellationToken);
        var codexOAuthTokenExchangeResult = await _codexOAuthClient.ExchangeAuthorizationCodeAsync(codexOAuthSession, codexOAuthCallbackPayload, cancellationToken);
        var codexAuthenticationDocument = CodexOAuthClient.CreateAuthenticationDocument(codexOAuthTokenExchangeResult);

        return new ProviderLoginResult
        {
            ProviderKind = ProviderKind,
            OutputText = _codexAuthenticationDocumentSerializer.Serialize(codexAuthenticationDocument),
            CompletionMessage = "Codex OAuth login completed.",
            IsAuthenticationDocument = true,
            ShouldPromptSaveCurrentAccount = false
        };
    }

    public async Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = string.IsNullOrWhiteSpace(storedAccountIdentifier)
            ? await LoadAuthenticationDocumentAsync(cancellationToken)
            : await LoadStoredAuthenticationDocumentAsync(storedAccountIdentifier, cancellationToken);
        var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(codexAuthenticationDocument, cancellationToken);

        return new ProviderUsageSnapshot
        {
            ProviderKind = ProviderKind,
            PlanType = codexUsageSnapshot.PlanType,
            EmailAddress = codexUsageSnapshot.EmailAddress,
            RawResponseText = codexUsageSnapshot.RawResponseText,
            FiveHour = new ProviderUsageWindow
            {
                UsedPercentage = codexUsageSnapshot.PrimaryWindow.UsedPercentage,
                RemainingPercentage = codexUsageSnapshot.PrimaryWindow.RemainingPercentage,
                ResetAfterSeconds = codexUsageSnapshot.PrimaryWindow.ResetAfterSeconds,
                ResetAt = CreateDateTimeOffset(codexUsageSnapshot.PrimaryWindow.ResetAtUnixSeconds)
            },
            SevenDay = new ProviderUsageWindow
            {
                UsedPercentage = codexUsageSnapshot.SecondaryWindow.UsedPercentage,
                RemainingPercentage = codexUsageSnapshot.SecondaryWindow.RemainingPercentage,
                ResetAfterSeconds = codexUsageSnapshot.SecondaryWindow.ResetAfterSeconds,
                ResetAt = CreateDateTimeOffset(codexUsageSnapshot.SecondaryWindow.ResetAtUnixSeconds)
            }
        };
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(cancellationToken);
        var codexModelDefinitions = await _codexModelsClient.GetModelsAsync(codexAuthenticationDocument, cancellationToken);
        return codexModelDefinitions.Select(codexModelDefinition => $"{codexModelDefinition.Identifier} ({codexModelDefinition.SourcePath})").ToArray();
    }

    public async Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model) || string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The response command requires both --model and --text.");

        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(cancellationToken);
        var codexResponseRequest = new CodexResponseRequest
        {
            Model = providerResponseRequest.Model,
            InputText = providerResponseRequest.Text,
            Instructions = providerResponseRequest.Instructions,
            Store = false
        };

        var codexResponseResult = await _codexResponsesClient.CreateResponseAsync(codexAuthenticationDocument, codexResponseRequest, false, cancellationToken);
        return new ProviderResponseResult
        {
            ProviderKind = ProviderKind,
            OutputText = codexResponseResult.OutputText,
            RawResponseText = codexResponseResult.RawResponseText
        };
    }

    public async IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model) || string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The stream-response command requires both --model and --text.");

        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(cancellationToken);
        var codexResponseRequest = new CodexResponseRequest
        {
            Model = providerResponseRequest.Model,
            InputText = providerResponseRequest.Text,
            Instructions = providerResponseRequest.Instructions,
            Stream = true,
            Store = false
        };

        await foreach (var codexResponseStreamEvent in _codexResponsesClient.StreamResponseAsync(codexAuthenticationDocument, codexResponseRequest, false, cancellationToken))
        {
            yield return new ProviderResponseStreamEvent
            {
                EventName = codexResponseStreamEvent.EventName,
                Data = codexResponseStreamEvent.Data,
                PayloadNode = codexResponseStreamEvent.PayloadNode,
                IsTerminal = codexResponseStreamEvent.IsTerminal
            };
        }
    }

    public async Task<IReadOnlyList<StoredProviderAccount>> ListStoredAccountsAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var currentIdentityProfile = await TryGetCurrentIdentityAsync(cancellationToken);

        return storedProviderAccounts
            .Select(storedProviderAccount => CloneStoredProviderAccount(storedProviderAccount, currentIdentityProfile is not null && IsSameCodexAccount(storedProviderAccount, currentIdentityProfile) || storedProviderAccount.IsActive))
            .ToArray();
    }

    public async Task<StoredProviderAccount> SaveCurrentAccountAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var authenticationDocumentJson = await File.ReadAllTextAsync(ResolveAuthenticationFilePath(), cancellationToken);
        var codexAuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentJson);
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var existingStoredProviderAccount = storedProviderAccounts.FirstOrDefault(storedProviderAccount => IsSameCodexAccount(storedProviderAccount, codexAuthenticationDocument));
        var slotNumber = existingStoredProviderAccount?.SlotNumber ?? GetNextSlotNumber(storedProviderAccounts);
        var storedAccountIdentifier = existingStoredProviderAccount?.StoredAccountIdentifier ?? slotNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var storedProviderAccount = CreateStoredProviderAccount(codexAuthenticationDocument, storedAccountIdentifier, slotNumber, true);
        var codexStoredAccountPayload = new CodexStoredAccountPayload { AuthenticationDocumentJson = authenticationDocumentJson };
        var payloadJson = JsonSerializer.Serialize(codexStoredAccountPayload, ProviderJsonSerializerOptions.Default);

        await providerSnapshotStore.SaveAsync(storedProviderAccount, payloadJson, cancellationToken);
        await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        return storedProviderAccount;
    }

    public async Task<StoredProviderAccount> ActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        var codexStoredAccountPayload = await LoadCodexStoredAccountPayloadAsync(providerSnapshotStore, storedAccountIdentifier, cancellationToken);
        await WriteTextAtomicallyAsync(ResolveAuthenticationFilePath(), codexStoredAccountPayload.AuthenticationDocumentJson, cancellationToken);
        await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var activatedStoredProviderAccount = storedProviderAccounts.FirstOrDefault(storedProviderAccount => string.Equals(storedProviderAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
        if (activatedStoredProviderAccount is null) throw new ProviderActionRequiredException($"The stored Codex account slot was not found: {storedAccountIdentifier}");
        return CloneStoredProviderAccount(activatedStoredProviderAccount, true);
    }

    public Task DeleteStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default) => providerSnapshotStore.DeleteAsync(ProviderKind, storedAccountIdentifier, cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<CodexAuthenticationDocument> LoadAuthenticationDocumentAsync(CancellationToken cancellationToken)
    {
        var authenticationFilePath = ResolveAuthenticationFilePath();
        var authenticationDocumentText = await File.ReadAllTextAsync(authenticationFilePath, cancellationToken);
        return CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
    }

    private async Task<CodexAuthenticationDocument> LoadStoredAuthenticationDocumentAsync(string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var codexStoredAccountPayload = await LoadCodexStoredAccountPayloadAsync(_providerSnapshotStore ?? throw new NotSupportedException("Stored account usage requires a snapshot store."), storedAccountIdentifier, cancellationToken);
        return CodexAuthenticationDocumentSerializer.Parse(codexStoredAccountPayload.AuthenticationDocumentJson);
    }

    private static async Task<CodexStoredAccountPayload> LoadCodexStoredAccountPayloadAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var payloadJson = await providerSnapshotStore.GetPayloadJsonAsync(CliProviderKind.Codex, storedAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ProviderActionRequiredException($"The stored Codex account slot was not found: {storedAccountIdentifier}");

        var codexStoredAccountPayload = JsonSerializer.Deserialize<CodexStoredAccountPayload>(payloadJson, ProviderJsonSerializerOptions.Default);
        if (codexStoredAccountPayload is null || string.IsNullOrWhiteSpace(codexStoredAccountPayload.AuthenticationDocumentJson)) throw new ProviderActionRequiredException($"The stored Codex account slot is invalid: {storedAccountIdentifier}");
        return codexStoredAccountPayload;
    }

    private async Task<ProviderIdentityProfile?> TryGetCurrentIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetCurrentIdentityAsync(cancellationToken);
        }
        catch { return null; }
    }

    private string ResolveAuthenticationFilePath()
    {
        if (!string.IsNullOrWhiteSpace(InputFilePathOverride)) return Path.GetFullPath(InputFilePathOverride);

        var defaultAuthenticationFilePath = BuildDefaultAuthenticationFilePath();
        if (File.Exists(defaultAuthenticationFilePath)) return defaultAuthenticationFilePath;

        throw new ProviderActionRequiredException("The Codex authentication document could not be found. Pass --input <path> to specify it explicitly.");
    }

    private string BuildDefaultAuthenticationFilePath() => Path.Combine(_codexApiClientOptions.CodexHomeDirectoryPath, "auth.json");

    private static ProviderIdentityProfile CreateIdentityProfile(CodexAuthenticationDocument codexAuthenticationDocument)
    {
        var codexIdentityProfile = codexAuthenticationDocument.TryReadIdentityProfile();
        var emailAddress = codexIdentityProfile?.EmailAddress ?? codexAuthenticationDocument.EmailAddress;
        var accountIdentifier = codexIdentityProfile?.AccountIdentifier ?? codexAuthenticationDocument.GetEffectiveAccountIdentifier();

        return new ProviderIdentityProfile
        {
            ProviderKind = CliProviderKind.Codex,
            EmailAddress = emailAddress,
            DisplayName = emailAddress,
            AccountIdentifier = accountIdentifier,
            OrganizationIdentifier = "",
            OrganizationName = "",
            PlanType = codexIdentityProfile?.PlanType ?? "",
            AccessTokenPreview = BuildAccessTokenPreview(codexAuthenticationDocument.GetEffectiveAccessToken()),
            ExpirationText = codexAuthenticationDocument.ExpirationText,
            IsLoggedIn = !string.IsNullOrWhiteSpace(codexAuthenticationDocument.GetEffectiveAccessToken()) && !string.IsNullOrWhiteSpace(accountIdentifier)
        };
    }

    private static StoredProviderAccount CreateStoredProviderAccount(CodexAuthenticationDocument codexAuthenticationDocument, string storedAccountIdentifier, int slotNumber, bool isActive)
    {
        var identityProfile = CreateIdentityProfile(codexAuthenticationDocument);
        return new StoredProviderAccount
        {
            ProviderKind = CliProviderKind.Codex,
            StoredAccountIdentifier = storedAccountIdentifier,
            SlotNumber = slotNumber,
            EmailAddress = identityProfile.EmailAddress,
            DisplayName = identityProfile.DisplayName,
            AccountIdentifier = identityProfile.AccountIdentifier,
            OrganizationIdentifier = "",
            OrganizationName = "",
            PlanType = identityProfile.PlanType,
            IsActive = isActive,
            IsTokenExpired = false,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

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

    private static bool IsSameCodexAccount(StoredProviderAccount storedProviderAccount, CodexAuthenticationDocument codexAuthenticationDocument)
    {
        var identityProfile = CreateIdentityProfile(codexAuthenticationDocument);
        return IsSameCodexAccount(storedProviderAccount, identityProfile);
    }

    private static bool IsSameCodexAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile identityProfile)
    {
        if (!string.IsNullOrWhiteSpace(storedProviderAccount.AccountIdentifier) && !string.IsNullOrWhiteSpace(identityProfile.AccountIdentifier))
        {
            return string.Equals(storedProviderAccount.AccountIdentifier, identityProfile.AccountIdentifier, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(storedProviderAccount.EmailAddress) && string.Equals(storedProviderAccount.EmailAddress, identityProfile.EmailAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetNextSlotNumber(IReadOnlyList<StoredProviderAccount> storedProviderAccounts) => storedProviderAccounts.Count == 0 ? 1 : storedProviderAccounts.Max(storedProviderAccount => storedProviderAccount.SlotNumber) + 1;

    private static DateTimeOffset? CreateDateTimeOffset(long unixSeconds)
    {
        if (unixSeconds < 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch { return null; }
    }

    private static bool TryOpenBrowser(Uri address)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = address.ToString(),
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
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
}
