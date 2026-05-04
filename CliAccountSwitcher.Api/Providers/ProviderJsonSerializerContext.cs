using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Claude;
using CliAccountSwitcher.Api.Providers.Codex;
using CliAccountSwitcher.Api.Storage;
using System.Text.Json.Serialization;

namespace CliAccountSwitcher.Api.Providers;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(CodexStoredAccountPayload))]
[JsonSerializable(typeof(ClaudeCodeStoredAccountPayload))]
[JsonSerializable(typeof(ProviderSnapshotManifestDocument))]
[JsonSerializable(typeof(StoredProviderAccount))]
[JsonSerializable(typeof(ProviderUsageSnapshot))]
[JsonSerializable(typeof(ProviderUsageWindow))]
[JsonSerializable(typeof(List<StoredProviderAccount>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
internal sealed partial class ProviderJsonSerializerContext : JsonSerializerContext
{
}
