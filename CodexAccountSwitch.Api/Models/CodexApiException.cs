using System;
using System.Net;

namespace CodexAccountSwitch.Api.Models;

public sealed class CodexApiException(string message, HttpStatusCode? statusCode = null, string? responseBody = null, Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public string? ResponseBody { get; } = responseBody;
}
