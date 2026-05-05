using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Codex.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure;
using CliAccountSwitcher.Api.Providers.Codex.Models;
using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Models.Responses;
using CliAccountSwitcher.Api.Providers.Serialization;

namespace CliAccountSwitcher.Api.Providers.Codex;

public sealed class CodexProviderAdapter : ProviderAdapterBase<CodexAccountState, CodexStoredAccountPayload>, IDisposable
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

    public override CliProviderKind ProviderKind => CliProviderKind.Codex;

    public override string DisplayName => "Codex";

    public override ProviderCapabilities Capabilities { get; } = new()
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

    public override string? GetDefaultInputFilePath() => BuildDefaultAuthenticationFilePath();

    public override async Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
    {
        var codexAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return CreateIdentityProfile(codexAccountState.AuthenticationDocument);
    }

    public override Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default) => Task.FromResult(CodexAuthenticationDocumentSerializer.Normalize(authenticationDocumentText));

    public override async Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default)
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

    public override async Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = string.IsNullOrWhiteSpace(storedAccountIdentifier)
            ? (await ReadLiveAccountStateAsync(cancellationToken)).AuthenticationDocument
            : CreateLiveAccountState(await LoadCodexStoredAccountPayloadAsync(storedAccountIdentifier, cancellationToken)).AuthenticationDocument;
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

    public override async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var codexAuthenticationDocument = (await ReadLiveAccountStateAsync(cancellationToken)).AuthenticationDocument;
        var codexModelDefinitions = await _codexModelsClient.GetModelsAsync(codexAuthenticationDocument, cancellationToken);
        return codexModelDefinitions.Select(codexModelDefinition => $"{codexModelDefinition.Identifier} ({codexModelDefinition.SourcePath})").ToArray();
    }

    public override async Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model) || string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The response command requires both --model and --text.");

        var codexAuthenticationDocument = (await ReadLiveAccountStateAsync(cancellationToken)).AuthenticationDocument;
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

    public override async IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerResponseRequest);
        if (string.IsNullOrWhiteSpace(providerResponseRequest.Model) || string.IsNullOrWhiteSpace(providerResponseRequest.Text)) throw new InvalidOperationException("The stream-response command requires both --model and --text.");

        var codexAuthenticationDocument = (await ReadLiveAccountStateAsync(cancellationToken)).AuthenticationDocument;
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

    public void Dispose() => _httpClient.Dispose();

    protected override async Task<CodexAccountState> ReadLiveAccountStateAsync(CancellationToken cancellationToken)
    {
        var authenticationDocumentJson = await File.ReadAllTextAsync(ResolveAuthenticationFilePath(), cancellationToken);
        return CreateCodexAccountState(authenticationDocumentJson);
    }

    protected override CodexAccountState CreateLiveAccountState(ProviderAccountDocumentSet providerAccountDocumentSet)
    {
        if (string.IsNullOrWhiteSpace(providerAccountDocumentSet.AuthenticationDocumentText)) throw new ProviderActionRequiredException("The Codex authentication document is required.");
        return CreateCodexAccountState(providerAccountDocumentSet.AuthenticationDocumentText);
    }

    protected override CodexAccountState CreateLiveAccountState(CodexStoredAccountPayload storedAccountPayload)
    {
        if (storedAccountPayload is null || string.IsNullOrWhiteSpace(storedAccountPayload.AuthenticationDocumentJson)) throw new ProviderActionRequiredException("The stored Codex account payload is invalid.");
        return CreateCodexAccountState(storedAccountPayload.AuthenticationDocumentJson);
    }

    protected override CodexStoredAccountPayload CreateStoredAccountPayload(CodexAccountState liveAccountState)
        => new()
        {
            AuthenticationDocumentJson = liveAccountState.AuthenticationDocumentJson
        };

    protected override string SerializeStoredAccountPayload(CodexStoredAccountPayload storedAccountPayload) => JsonSerializer.Serialize(storedAccountPayload, ProviderJsonSerializerOptions.Default);

    protected override CodexStoredAccountPayload DeserializeStoredAccountPayload(string payloadJson, string storedAccountIdentifier)
    {
        var codexStoredAccountPayload = JsonSerializer.Deserialize<CodexStoredAccountPayload>(payloadJson, ProviderJsonSerializerOptions.Default);
        if (codexStoredAccountPayload is null || string.IsNullOrWhiteSpace(codexStoredAccountPayload.AuthenticationDocumentJson)) throw new ProviderActionRequiredException($"The stored Codex account slot is invalid: {storedAccountIdentifier}");
        return codexStoredAccountPayload;
    }

    protected override StoredProviderAccount CreateStoredProviderAccount(CodexAccountState liveAccountState, string storedAccountIdentifier, int slotNumber, bool isActive)
    {
        var identityProfile = CreateIdentityProfile(liveAccountState.AuthenticationDocument);
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
            LastUpdated = DateTimeOffset.UtcNow,
            LastProviderUsageSnapshot = new ProviderUsageSnapshot { ProviderKind = CliProviderKind.Codex }
        };
    }

    protected override bool IsSameAccount(StoredProviderAccount storedProviderAccount, CodexAccountState liveAccountState) => IsSameAccount(storedProviderAccount, CreateIdentityProfile(liveAccountState.AuthenticationDocument));

    protected override bool IsSameAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile providerIdentityProfile)
    {
        if (!string.IsNullOrWhiteSpace(storedProviderAccount.AccountIdentifier) && !string.IsNullOrWhiteSpace(providerIdentityProfile.AccountIdentifier))
        {
            return string.Equals(storedProviderAccount.AccountIdentifier, providerIdentityProfile.AccountIdentifier, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(storedProviderAccount.EmailAddress) && string.Equals(storedProviderAccount.EmailAddress, providerIdentityProfile.EmailAddress, StringComparison.OrdinalIgnoreCase);
    }

    protected override async Task WriteLiveAccountStateAsync(CodexAccountState liveAccountState, CancellationToken cancellationToken) => await WriteTextAtomicallyAsync(ResolveAuthenticationFilePath(), liveAccountState.AuthenticationDocumentJson, cancellationToken);

    private async Task<CodexStoredAccountPayload> LoadCodexStoredAccountPayloadAsync(string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var providerSnapshotStore = _providerSnapshotStore ?? throw new NotSupportedException("Stored account usage requires a snapshot store.");
        var payloadJson = await providerSnapshotStore.GetPayloadJsonAsync(CliProviderKind.Codex, storedAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ProviderActionRequiredException($"The stored Codex account slot was not found: {storedAccountIdentifier}");
        return DeserializeStoredAccountPayload(payloadJson, storedAccountIdentifier);
    }

    private string ResolveAuthenticationFilePath()
    {
        if (!string.IsNullOrWhiteSpace(InputFilePathOverride)) return Path.GetFullPath(InputFilePathOverride);

        var defaultAuthenticationFilePath = BuildDefaultAuthenticationFilePath();
        if (File.Exists(defaultAuthenticationFilePath)) return defaultAuthenticationFilePath;

        throw new ProviderActionRequiredException("The Codex authentication document could not be found. Pass --input <path> to specify it explicitly.");
    }

    private string BuildDefaultAuthenticationFilePath() => Path.Combine(_codexApiClientOptions.CodexHomeDirectoryPath, "auth.json");

    private static CodexAccountState CreateCodexAccountState(string authenticationDocumentJson)
        => new()
        {
            AuthenticationDocumentJson = authenticationDocumentJson,
            AuthenticationDocument = CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentJson)
        };

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
