using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CliAccountSwitcher.Api.Providers.Codex.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure.Http;
using CliAccountSwitcher.Api.Providers.Codex.Models;
using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Models.OAuth;

namespace CliAccountSwitcher.Api.Providers.Codex;

public sealed class CodexOAuthClient(HttpClient httpClient, CodexApiClientOptions codexApiClientOptions, CodexRequestMessageFactory codexRequestMessageFactory)
{
    public CodexOAuthSession CreateSession()
    {
        const int maximumDynamicRedirectPortAttemptCount = 16;

        var redirectPorts = codexApiClientOptions.UseDynamicOAuthRedirectPort
            ? CodexOAuthLoopbackPortAllocator.FindAvailablePorts(codexApiClientOptions.MinimumOAuthRedirectPort, codexApiClientOptions.MaximumOAuthRedirectPort, maximumDynamicRedirectPortAttemptCount)
            : [CodexOAuthLoopbackPortAllocator.ValidateFixedPort(codexApiClientOptions.OAuthRedirectPort)];
        var lastException = (CodexApiException?)null;

        foreach (var redirectPort in redirectPorts)
        {
            var codexOAuthSession = CreateSessionForPort(redirectPort);

            try
            {
                codexOAuthSession.StartListening();
                return codexOAuthSession;
            }
            catch (CodexApiException exception)
            {
                codexOAuthSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
                lastException = exception;
                if (!codexApiClientOptions.UseDynamicOAuthRedirectPort) throw;
            }
        }

        if (!codexApiClientOptions.UseDynamicOAuthRedirectPort) throw new CodexApiException($"The OAuth callback listener could not start on the configured port {codexApiClientOptions.OAuthRedirectPort}.", null, null, lastException);
        throw new CodexApiException($"The OAuth callback listener could not start on any port in the configured loopback range {codexApiClientOptions.MinimumOAuthRedirectPort}-{codexApiClientOptions.MaximumOAuthRedirectPort}.", null, null, lastException);
    }

    public async Task<CodexOAuthTokenExchangeResult> ExchangeAuthorizationCodeAsync(CodexOAuthSession codexOAuthSession, CodexOAuthCallbackPayload codexOAuthCallbackPayload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codexOAuthSession);
        ArgumentNullException.ThrowIfNull(codexOAuthCallbackPayload);

        codexOAuthSession.ValidateCallback(codexOAuthCallbackPayload);
        var postContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = codexApiClientOptions.OAuthClientId,
            ["code"] = codexOAuthCallbackPayload.AuthorizationCode,
            ["redirect_uri"] = codexOAuthSession.RedirectAddress.ToString(),
            ["code_verifier"] = codexOAuthSession.CodeVerifier
        });

        return await SendTokenExchangeRequestAsync(postContent, cancellationToken);
    }

    public async Task<CodexOAuthTokenExchangeResult> ExchangeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new CodexApiException("The refresh token is required.");

        var postContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = codexApiClientOptions.OAuthClientId,
            ["refresh_token"] = refreshToken
        });

        return await SendTokenExchangeRequestAsync(postContent, cancellationToken);
    }

    public static CodexAuthenticationDocument CreateAuthenticationDocument(CodexOAuthTokenExchangeResult codexOAuthTokenExchangeResult, DateTimeOffset? currentTime = null)
    {
        ArgumentNullException.ThrowIfNull(codexOAuthTokenExchangeResult);

        var effectiveCurrentTime = currentTime ?? codexOAuthTokenExchangeResult.IssuedAt;
        var expirationTime = effectiveCurrentTime.AddSeconds(codexOAuthTokenExchangeResult.ExpiresInSeconds);

        return new CodexAuthenticationDocument
        {
            AuthenticationMode = "chatgpt",
            OpenAiApiKey = null,
            Tokens = new CodexAuthenticationTokenDocument
            {
                IdentityToken = codexOAuthTokenExchangeResult.IdentityToken,
                AccessToken = codexOAuthTokenExchangeResult.AccessToken,
                RefreshToken = codexOAuthTokenExchangeResult.RefreshToken,
                AccountIdentifier = codexOAuthTokenExchangeResult.AccountIdentifier
            },
            IdentityToken = codexOAuthTokenExchangeResult.IdentityToken,
            AccessToken = codexOAuthTokenExchangeResult.AccessToken,
            RefreshToken = codexOAuthTokenExchangeResult.RefreshToken,
            AccountIdentifier = codexOAuthTokenExchangeResult.AccountIdentifier,
            LastRefreshText = effectiveCurrentTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EmailAddress = string.IsNullOrWhiteSpace(codexOAuthTokenExchangeResult.EmailAddress) ? "unknown" : codexOAuthTokenExchangeResult.EmailAddress,
            AuthenticationType = "codex",
            ExpirationText = expirationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
    }

    private async Task<CodexOAuthTokenExchangeResult> SendTokenExchangeRequestAsync(FormUrlEncodedContent postContent, CancellationToken cancellationToken)
    {
        using var httpRequestMessage = codexRequestMessageFactory.CreateOAuthTokenExchangeRequest(postContent);
        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await CodexHttpResponseValidator.EnsureSuccessAndReadContentAsync(httpResponseMessage, cancellationToken);
        return ParseTokenExchangeResult(responseText);
    }

    private CodexOAuthSession CreateSessionForPort(int redirectPort)
    {
        var codeVerifier = CreateRandomBase64UrlText(96);
        var codeChallenge = CreateSha256Base64UrlText(codeVerifier);
        var state = CreateRandomBase64UrlText(32);
        var redirectAddress = codexRequestMessageFactory.BuildOAuthRedirectUri(redirectPort);
        var authorizationAddress = codexRequestMessageFactory.BuildOAuthAuthorizationUri(state, codeChallenge, redirectAddress);
        return new CodexOAuthSession(authorizationAddress, redirectAddress, state, codeVerifier, codeChallenge, codexApiClientOptions.OAuthTimeout);
    }

    private static CodexOAuthTokenExchangeResult ParseTokenExchangeResult(string responseText)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var rootElement = jsonDocument.RootElement;

        var accessToken = CodexJsonElementReader.ReadStringOrNull(rootElement, "access_token") ?? "";
        var identityToken = CodexJsonElementReader.ReadStringOrNull(rootElement, "id_token") ?? "";
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(identityToken)) throw new CodexApiException("The OAuth token response does not contain the required tokens.", null, responseText);

        var refreshToken = CodexJsonElementReader.ReadStringOrNull(rootElement, "refresh_token") ?? "";
        var expiresInSeconds = CodexJsonElementReader.ReadInt32OrNull(rootElement, "expires_in") ?? 3600;
        var identityProfile = CodexJsonWebTokenParser.TryReadIdentityProfile(identityToken);
        var accountIdentifier = CodexJsonElementReader.ReadStringOrNull(rootElement, "account_id") ?? identityProfile?.AccountIdentifier ?? "";
        if (string.IsNullOrWhiteSpace(accountIdentifier)) throw new CodexApiException("The OAuth token response does not contain an account identifier.", null, responseText);

        return new CodexOAuthTokenExchangeResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IdentityToken = identityToken,
            AccountIdentifier = accountIdentifier,
            EmailAddress = identityProfile?.EmailAddress ?? "",
            PlanType = identityProfile?.PlanType ?? "",
            ExpiresInSeconds = expiresInSeconds,
            IssuedAt = DateTimeOffset.UtcNow,
            RawResponseText = responseText
        };
    }

    private static string CreateRandomBase64UrlText(int byteCount)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreateSha256Base64UrlText(string value)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hashBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
