using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.OAuth;

namespace CliAccountSwitcher.Api.Authentication;

public sealed class CodexOAuthCallbackListener : IAsyncDisposable
{
    private readonly Uri _redirectAddress;
    private readonly TimeSpan _timeout;
    private HttpListener? _httpListener;
    private bool _disposed;

    public CodexOAuthCallbackListener(Uri redirectAddress, TimeSpan timeout)
    {
        _redirectAddress = redirectAddress;
        _timeout = timeout;
    }

    public Uri RedirectAddress => _redirectAddress;

    public void StartListening()
    {
        ThrowIfDisposed();
        EnsureListenerStarted();
    }

    public async Task<CodexOAuthCallbackPayload> ListenAsync(Func<string, CodexOAuthCallbackPayload> callbackPayloadFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callbackPayloadFactory);
        ThrowIfDisposed();
        EnsureListenerStarted();

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(_timeout);
        using var cancellationRegistration = timeoutCancellationTokenSource.Token.Register(StopListener);

        try
        {
            while (true)
            {
                var listenerContext = await _httpListener!.GetContextAsync();
                if (!IsExpectedCallbackPath(listenerContext.Request.Url))
                {
                    await WriteResponseAsync(listenerContext.Response, 404, "<html><body><h1>Not Found</h1></body></html>", timeoutCancellationTokenSource.Token);
                    continue;
                }

                var callbackPayload = callbackPayloadFactory(listenerContext.Request.Url?.ToString() ?? "");
                var htmlText = callbackPayload.HasError
                    ? "<html><body><h1>Authorization Failed</h1><p>You can close this window now.</p></body></html>"
                    : "<html><body><h1>Authorization Successful</h1><p>You can close this window now.</p></body></html>";
                await WriteResponseAsync(listenerContext.Response, callbackPayload.HasError ? 400 : 200, htmlText, timeoutCancellationTokenSource.Token);
                return callbackPayload;
            }
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
        {
            if (timeoutCancellationTokenSource.IsCancellationRequested) throw new OperationCanceledException("The OAuth callback listener timed out or was canceled.", exception, timeoutCancellationTokenSource.Token);
            throw new CodexApiException($"The OAuth callback listener failed on {_redirectAddress}.", null, null, exception);
        }
        finally
        {
            StopListener();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        StopListener();
        return ValueTask.CompletedTask;
    }

    private void EnsureListenerStarted()
    {
        if (_httpListener is not null) return;

        if (!string.Equals(_redirectAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) throw new CodexApiException("The OAuth callback listener only supports the http scheme.");
        if (!string.Equals(_redirectAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase) && !string.Equals(_redirectAddress.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)) throw new CodexApiException("The OAuth callback listener only supports localhost loopback addresses.");
        _ = CodexOAuthLoopbackPortAllocator.ValidateFixedPort(_redirectAddress.Port);

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"{_redirectAddress.Scheme}://{_redirectAddress.Host}:{_redirectAddress.Port}/");
            _httpListener.Start();
        }
        catch (Exception exception)
        {
            _httpListener = null;
            throw new CodexApiException($"The OAuth callback listener could not start on {_redirectAddress}.", null, null, exception);
        }
    }

    private bool IsExpectedCallbackPath(Uri? requestAddress)
        => requestAddress is not null && string.Equals(requestAddress.AbsolutePath.TrimEnd('/'), _redirectAddress.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static async Task WriteResponseAsync(HttpListenerResponse httpListenerResponse, int statusCode, string responseText, CancellationToken cancellationToken)
    {
        var responseBytes = Encoding.UTF8.GetBytes(responseText);
        httpListenerResponse.StatusCode = statusCode;
        httpListenerResponse.ContentType = "text/html; charset=utf-8";
        httpListenerResponse.ContentEncoding = Encoding.UTF8;
        httpListenerResponse.ContentLength64 = responseBytes.LongLength;
        await httpListenerResponse.OutputStream.WriteAsync(responseBytes, cancellationToken);
        httpListenerResponse.OutputStream.Close();
    }

    private void StopListener()
    {
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch
        {
        }
        finally
        {
            _httpListener = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CodexOAuthCallbackListener));
    }
}
