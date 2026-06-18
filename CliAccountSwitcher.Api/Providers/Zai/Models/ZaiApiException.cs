using System.Net;

namespace CliAccountSwitcher.Api.Providers.Zai.Models;

public sealed class ZaiApiException(string message, HttpStatusCode httpStatusCode = HttpStatusCode.OK, int? applicationCode = null, string? responseText = null) : Exception(message)
{
    public HttpStatusCode HttpStatusCode { get; } = httpStatusCode;

    public int? ApplicationCode { get; } = applicationCode;

    public string? ResponseText { get; } = responseText;
}
