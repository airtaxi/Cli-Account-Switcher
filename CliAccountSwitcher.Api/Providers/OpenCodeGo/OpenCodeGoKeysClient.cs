using System.Net;
using System.Text.RegularExpressions;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models;

namespace CliAccountSwitcher.Api.Providers.OpenCodeGo;

public sealed class OpenCodeGoKeysClient(HttpClient httpClient)
{
    private static readonly Regex s_keyObjectPattern = new(@"\{id:""key_[^""]*"",name:""([^""]*)"",key:""(sk-[^""]+)"",timeUsed:null,userID:""[^""]*"",(?:email:""([^""]*)"",)?keyDisplay:""([^""]*)""\}", RegexOptions.Compiled);

    public async Task<OpenCodeGoKeyInfo?> GetFirstApiKeyAsync(string workspaceId, string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) throw new ArgumentException("The workspace ID is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(authCookie)) throw new ArgumentException("The auth cookie is required.", nameof(authCookie));

        var keysPagePath = string.Format(OpenCodeGoApiConventions.KeysPagePathTemplate, workspaceId);
        var requestUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, keysPagePath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Add("Cookie", $"{OpenCodeGoApiConventions.AuthCookieName}={authCookie}");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        if (httpResponseMessage.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new OpenCodeGoAuthExpiredException("The OpenCode Go auth cookie has expired.");

        httpResponseMessage.EnsureSuccessStatusCode();
        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        return ParseFirstKey(responseText);
    }

    private static OpenCodeGoKeyInfo? ParseFirstKey(string htmlText)
    {
        var match = s_keyObjectPattern.Match(htmlText);
        if (!match.Success) return null;

        return new OpenCodeGoKeyInfo
        {
            Name = match.Groups[1].Value,
            Key = match.Groups[2].Value,
            Email = match.Groups[3].Success ? match.Groups[3].Value : "",
            KeyDisplay = match.Groups[4].Value
        };
    }
}
