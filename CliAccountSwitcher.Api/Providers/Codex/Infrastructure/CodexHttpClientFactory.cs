using System.Net;
using System.Net.Http;

namespace CliAccountSwitcher.Api.Providers.Codex.Infrastructure;

public static class CodexHttpClientFactory
{
    public static HttpClient CreateDefault()
    {
        var httpClientHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        return new HttpClient(httpClientHandler, true);
    }
}
