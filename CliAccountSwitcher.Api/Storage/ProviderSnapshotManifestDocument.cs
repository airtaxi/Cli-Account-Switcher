using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Storage;

internal sealed class ProviderSnapshotManifestDocument
{
    public List<StoredProviderAccount> Accounts { get; set; } = [];

    public Dictionary<string, string?> ActiveStoredAccountIdentifiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
