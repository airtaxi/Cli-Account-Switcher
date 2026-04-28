using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.OAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CliAccountSwitcher.Api.Authentication;

public sealed class CodexOAuthSession : IAsyncDisposable
{
    private readonly CodexOAuthCallbackListener _codexOAuthCallbackListener;
    private bool _disposed;

    internal CodexOAuthSession(Uri authorizationAddress, Uri redirectAddress, string state, string codeVerifier, string codeChallenge, TimeSpan timeout)
    {
        AuthorizationAddress = authorizationAddress;
        RedirectAddress = redirectAddress;
        State = state;
        CodeVerifier = codeVerifier;
        CodeChallenge = codeChallenge;
        _codexOAuthCallbackListener = new CodexOAuthCallbackListener(redirectAddress, timeout);
    }

    public Uri AuthorizationAddress { get; }

    public Uri RedirectAddress { get; }

    public string State { get; }

    public string CodeVerifier { get; }

    public string CodeChallenge { get; }

    public void StartListening()
    {
        ThrowIfDisposed();
        _codexOAuthCallbackListener.StartListening();
    }

    public async Task<CodexOAuthCallbackPayload> WaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _codexOAuthCallbackListener.ListenAsync(ParseCallbackAddress, cancellationToken);
    }

    public CodexOAuthCallbackPayload ParseCallbackAddress(string callbackAddress)
    {
        if (!Uri.TryCreate(callbackAddress, UriKind.Absolute, out var callbackUri)) throw new InvalidDataException("The callback address is not a valid absolute URI.");

        var queryParameters = ParseQueryParameters(callbackUri.Query);
        var callbackPayload = new CodexOAuthCallbackPayload
        {
            AuthorizationCode = ReadQueryValue(queryParameters, "code"),
            State = ReadQueryValue(queryParameters, "state"),
            Error = ReadQueryValue(queryParameters, "error"),
            ErrorDescription = ReadQueryValue(queryParameters, "error_description")
        };
        return callbackPayload;
    }

    public void ValidateCallback(CodexOAuthCallbackPayload codexOAuthCallbackPayload)
    {
        ArgumentNullException.ThrowIfNull(codexOAuthCallbackPayload);
        if (codexOAuthCallbackPayload.HasError) throw new CodexApiException($"The OAuth authorization failed: {codexOAuthCallbackPayload.Error}");
        if (string.IsNullOrWhiteSpace(codexOAuthCallbackPayload.AuthorizationCode)) throw new CodexApiException("The OAuth callback does not contain an authorization code.");
        if (!string.Equals(State, codexOAuthCallbackPayload.State, StringComparison.Ordinal)) throw new CodexApiException("The OAuth callback state does not match the current session.");
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return _codexOAuthCallbackListener.DisposeAsync();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CodexOAuthSession));
    }

    private static Dictionary<string, string> ParseQueryParameters(string queryText)
    {
        var queryParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedQueryText = queryText.TrimStart('?');
        if (string.IsNullOrWhiteSpace(normalizedQueryText)) return queryParameters;

        foreach (var pairText in normalizedQueryText.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pairText.IndexOf('=');
            if (separatorIndex < 0) continue;

            var propertyName = Uri.UnescapeDataString(pairText[..separatorIndex]);
            var propertyValue = Uri.UnescapeDataString(pairText[(separatorIndex + 1)..]);
            if (string.IsNullOrWhiteSpace(propertyName)) continue;

            queryParameters[propertyName] = propertyValue;
        }

        return queryParameters;
    }

    private static string ReadQueryValue(Dictionary<string, string> queryParameters, string propertyName) => queryParameters.TryGetValue(propertyName, out var propertyValue) ? propertyValue : "";
}
