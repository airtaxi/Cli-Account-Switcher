using System.Text.Json;

namespace CliAccountSwitcher.Api.Providers;

internal static class ProviderJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } = ProviderJsonSerializerContext.Default.Options;
}
