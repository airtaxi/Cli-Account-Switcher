using System.Text.Json;

namespace CliAccountSwitcher.Api.Providers.Serialization;

internal static class ProviderJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } = ProviderJsonSerializerContext.Default.Options;
}
