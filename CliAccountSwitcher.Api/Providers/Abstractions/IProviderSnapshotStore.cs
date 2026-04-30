namespace CliAccountSwitcher.Api.Providers.Abstractions;

public interface IProviderSnapshotStore
{
    Task<IReadOnlyList<StoredProviderAccount>> GetStoredAccountsAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default);

    Task<string?> GetPayloadJsonAsync(CliProviderKind providerKind, string storedAccountIdentifier, CancellationToken cancellationToken = default);

    Task SaveAsync(StoredProviderAccount storedProviderAccount, string payloadJson, CancellationToken cancellationToken = default);

    Task DeleteAsync(CliProviderKind providerKind, string storedAccountIdentifier, CancellationToken cancellationToken = default);

    Task SetActiveStoredAccountIdentifierAsync(CliProviderKind providerKind, string? storedAccountIdentifier, CancellationToken cancellationToken = default);

    Task<string?> GetActiveStoredAccountIdentifierAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default);
}
