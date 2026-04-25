using System;
using System.Net.Http;
using System.Net.Http.Headers;
using CodexAccountSwitch.Api.Models;

namespace CodexAccountSwitch.Api.Infrastructure;

public sealed class CodexRequestMessageFactory(CodexApiClientOptions codexApiClientOptions, CodexClientMetadataProvider codexClientMetadataProvider)
{
    public HttpRequestMessage CreateCodexApiRequest(HttpMethod httpMethod, string requestPath, CodexRequestContext codexRequestContext, HttpContent? httpContent = null, string acceptHeaderValue = "application/json")
    {
        var requestUri = new Uri(CodexApiConventions.ChatGptBaseUri, requestPath);
        var httpRequestMessage = new HttpRequestMessage(httpMethod, requestUri)
        {
            Content = httpContent
        };
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codexRequestContext.AccessToken);
        httpRequestMessage.Headers.TryAddWithoutValidation("Accept", acceptHeaderValue);
        httpRequestMessage.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", codexRequestContext.AccountId);
        httpRequestMessage.Headers.TryAddWithoutValidation("Version", codexClientMetadataProvider.ReadCodexVersion());
        httpRequestMessage.Headers.TryAddWithoutValidation("Openai-Beta", CodexApiConventions.ResponsesBetaHeaderValue);
        httpRequestMessage.Headers.TryAddWithoutValidation("Session_id", Guid.NewGuid().ToString());
        httpRequestMessage.Headers.TryAddWithoutValidation("Originator", CodexApiConventions.CodexOriginator);
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", codexClientMetadataProvider.BuildCodexApiUserAgent());
        httpRequestMessage.Headers.ConnectionClose = true;
        return httpRequestMessage;
    }

    public HttpRequestMessage CreateCodexModelsRequest(CodexRequestContext codexRequestContext)
    {
        var clientVersion = codexClientMetadataProvider.ReadCodexVersion();
        var requestUri = new Uri(CodexApiConventions.ChatGptBaseUri, $"{CodexApiConventions.CodexModelsPath}?{CodexApiConventions.ClientVersionQueryParameterName}={Uri.EscapeDataString(clientVersion)}");
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codexRequestContext.AccessToken);
        httpRequestMessage.Headers.TryAddWithoutValidation("Accept", "application/json");
        httpRequestMessage.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", codexRequestContext.AccountId);
        httpRequestMessage.Headers.TryAddWithoutValidation("Version", clientVersion);
        httpRequestMessage.Headers.TryAddWithoutValidation("Openai-Beta", CodexApiConventions.ResponsesBetaHeaderValue);
        httpRequestMessage.Headers.TryAddWithoutValidation("Session_id", Guid.NewGuid().ToString());
        httpRequestMessage.Headers.TryAddWithoutValidation("Originator", CodexApiConventions.CodexOriginator);
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", codexClientMetadataProvider.BuildCodexApiUserAgent());
        httpRequestMessage.Headers.ConnectionClose = true;
        return httpRequestMessage;
    }

    public HttpRequestMessage CreateUsageRequest(CodexRequestContext codexRequestContext)
    {
        var requestUri = new Uri(CodexApiConventions.ChatGptBaseUri, CodexApiConventions.UsagePath);
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codexRequestContext.AccessToken);
        httpRequestMessage.Headers.TryAddWithoutValidation("host", CodexApiConventions.ChatGptBaseUri.Host);
        httpRequestMessage.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", codexRequestContext.AccountId);
        httpRequestMessage.Headers.TryAddWithoutValidation("originator", CodexApiConventions.UsageOriginator);
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", codexClientMetadataProvider.BuildUsageUserAgent());
        httpRequestMessage.Headers.TryAddWithoutValidation("accept", "*/*");
        httpRequestMessage.Headers.TryAddWithoutValidation("accept-language", "*");
        httpRequestMessage.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        httpRequestMessage.Headers.ConnectionClose = false;
        return httpRequestMessage;
    }

    public HttpRequestMessage CreateOAuthTokenExchangeRequest(HttpContent httpContent)
    {
        var requestUri = new Uri(CodexApiConventions.OAuthBaseUri, CodexApiConventions.OAuthTokenPath);
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = httpContent
        };
        httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return httpRequestMessage;
    }

    public Uri BuildOAuthAuthorizationUri(string state, string codeChallenge, Uri redirectUri)
    {
        var queryString = string.Join("&",
        [
            $"client_id={Uri.EscapeDataString(codexApiClientOptions.OAuthClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(redirectUri.ToString())}",
            $"scope={Uri.EscapeDataString("openid email profile offline_access")}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
            "prompt=login",
            "id_token_add_organizations=true",
            "codex_cli_simplified_flow=true"
        ]);
        return new Uri(CodexApiConventions.OAuthBaseUri, $"{CodexApiConventions.OAuthAuthorizePath}?{queryString}");
    }

    public Uri BuildOAuthRedirectUri(int port) => new($"http://localhost:{port}{codexApiClientOptions.OAuthRedirectPath}");
}
