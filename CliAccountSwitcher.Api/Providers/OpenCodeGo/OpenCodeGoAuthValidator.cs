using System.Net;
using System.Text.Json;

namespace CliAccountSwitcher.Api.Providers.OpenCodeGo;

public sealed class OpenCodeGoAuthValidator(HttpClient httpClient)
{
    public async Task<bool> IsAuthCookieValidAsync(string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie)) return false;

        var requestUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, OpenCodeGoApiConventions.AuthStatusPath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Add("Cookie", $"{OpenCodeGoApiConventions.AuthCookieName}={authCookie}");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        if (!httpResponseMessage.IsSuccessStatusCode) return false;

        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(responseText) && !string.Equals(responseText.Trim(), "{}", StringComparison.Ordinal);
    }

    public async Task<string?> GetAccountEmailAsync(string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie)) return null;

        var requestUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, OpenCodeGoApiConventions.AuthStatusPath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Add("Cookie", $"{OpenCodeGoApiConventions.AuthCookieName}={authCookie}");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        if (!httpResponseMessage.IsSuccessStatusCode) return null;

        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        return ParseEmailFromAuthStatus(responseText);
    }

    private static string? ParseEmailFromAuthStatus(string responseText)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(responseText);
            if (!jsonDocument.RootElement.TryGetProperty("current", out var currentElement)) return null;
            var currentAccountId = currentElement.GetString();
            if (string.IsNullOrWhiteSpace(currentAccountId)) return null;

            if (!jsonDocument.RootElement.TryGetProperty("account", out var accountElement)) return null;
            if (!accountElement.TryGetProperty(currentAccountId, out var accountInfoElement)) return null;
            if (!accountInfoElement.TryGetProperty("email", out var emailElement)) return null;

            return emailElement.GetString();
        }
        catch { return null; }
    }
}
