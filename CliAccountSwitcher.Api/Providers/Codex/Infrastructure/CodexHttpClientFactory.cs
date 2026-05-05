using System.Net;
using System.Net.Http;

namespace CliAccountSwitcher.Api.Providers.Codex.Infrastructure;

public static class CodexHttpClientFactory
{
    public static HttpClient CreateDefault() => new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }, true);
}
