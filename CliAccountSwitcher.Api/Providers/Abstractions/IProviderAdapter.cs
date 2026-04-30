namespace CliAccountSwitcher.Api.Providers.Abstractions;

public interface IProviderAdapter
{
    CliProviderKind ProviderKind { get; }

    string DisplayName { get; }

    ProviderCapabilities Capabilities { get; }

    string? GetDefaultInputFilePath();

    Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default);

    Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default);

    Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default);

    Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredProviderAccount>> ListStoredAccountsAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default);

    Task<StoredProviderAccount> SaveCurrentAccountAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default);

    Task<StoredProviderAccount> ActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default);

    Task DeleteStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default);
}
