using System.Net;
using CliAccountSwitcher.Api.Models;

namespace CliAccountSwitcher.Api.Infrastructure.Http;

public static class CodexHttpResponseValidator
{
    public static async Task<string> ReadRequiredContentAsync(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        var responseBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(responseBody)) return responseBody;
        throw new CodexApiException("The response body is empty.", httpResponseMessage.StatusCode);
    }

    public static async Task<string> EnsureSuccessAndReadContentAsync(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        var responseBody = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        if (httpResponseMessage.IsSuccessStatusCode) return responseBody;
        throw new CodexApiException(BuildMessage(httpResponseMessage.StatusCode), httpResponseMessage.StatusCode, responseBody);
    }

    private static string BuildMessage(HttpStatusCode statusCode) => $"The Codex API request failed. HTTP {(int)statusCode} {statusCode}.";
}
