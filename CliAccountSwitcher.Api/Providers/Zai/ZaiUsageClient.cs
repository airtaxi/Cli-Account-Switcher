using System.Net.Http.Headers;
using CliAccountSwitcher.Api.Providers.Zai.Infrastructure.Http;
using CliAccountSwitcher.Api.Providers.Zai.Models;
using CliAccountSwitcher.Api.Providers.Zai.Models.Usage;

namespace CliAccountSwitcher.Api.Providers.Zai;

public sealed class ZaiUsageClient(HttpClient httpClient)
{
    public Task<ZaiUsageSnapshot> GetUsageAsync(string apiKey, CancellationToken cancellationToken = default) => GetUsageAsync(apiKey, false, cancellationToken);

    public async Task<ZaiUsageSnapshot> GetUsageAsync(string apiKey, bool preferChinaEndpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The Z.ai API key is required.", nameof(apiKey));

        if (preferChinaEndpoint) return await SendUsageRequestAsync(apiKey, ZaiApiConventions.ChinaBaseUri, true, cancellationToken);

        try { return await SendUsageRequestAsync(apiKey, ZaiApiConventions.GlobalBaseUri, false, cancellationToken); }
        catch (ZaiApiException exception) when (exception.ApplicationCode == 401) { return await SendUsageRequestAsync(apiKey, ZaiApiConventions.ChinaBaseUri, true, cancellationToken); }
    }

    public Task<ZaiUsageSnapshot> GetUsageAsync(ZaiChelperConfig chelperConfig, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chelperConfig);
        if (!chelperConfig.IsValid) throw new ArgumentException("The chelper config does not contain an API key.", nameof(chelperConfig));
        return GetUsageAsync(chelperConfig.ApiKey, chelperConfig.IsChinaPlan, cancellationToken);
    }

    private async Task<ZaiUsageSnapshot> SendUsageRequestAsync(string apiKey, Uri baseUri, bool usedChinaEndpoint, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(baseUri, ZaiApiConventions.QuotaLimitPath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequestMessage.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await ZaiHttpResponseValidator.EnsureSuccessAndReadContentAsync(httpResponseMessage, cancellationToken);
        return ZaiQuotaLimitResponse.Parse(responseText, (int)httpResponseMessage.StatusCode, usedChinaEndpoint);
    }
}
