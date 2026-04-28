using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CliAccountSwitcher.Api.Infrastructure;
using CliAccountSwitcher.Api.Infrastructure.Http;
using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.Authentication;
using CliAccountSwitcher.Api.Models.Usage;

namespace CliAccountSwitcher.Api;

public sealed class CodexUsageClient(HttpClient httpClient, CodexRequestMessageFactory codexRequestMessageFactory)
{
    public Task<CodexUsageSnapshot> GetUsageAsync(CodexAuthenticationDocument codexAuthenticationDocument, CancellationToken cancellationToken = default) => GetUsageAsync(codexAuthenticationDocument.CreateRequestContext(), cancellationToken);

    public async Task<CodexUsageSnapshot> GetUsageAsync(CodexRequestContext codexRequestContext, CancellationToken cancellationToken = default)
    {
        using var httpRequestMessage = codexRequestMessageFactory.CreateUsageRequest(codexRequestContext);
        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await CodexHttpResponseValidator.EnsureSuccessAndReadContentAsync(httpResponseMessage, cancellationToken);
        return ParseUsageSnapshot(responseText, (int)httpResponseMessage.StatusCode);
    }

    private static CodexUsageSnapshot ParseUsageSnapshot(string responseText, int httpStatusCode)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var rootElement = jsonDocument.RootElement;
        if (!CodexJsonElementReader.TryGetProperty(rootElement, "rate_limit", out var rateLimitElement) || rateLimitElement.ValueKind != JsonValueKind.Object) throw new CodexApiException("The usage response does not contain a rate_limit object.", null, responseText);

        return new CodexUsageSnapshot
        {
            PlanType = CodexJsonElementReader.ReadStringOrNull(rootElement, "plan_type") ?? "",
            EmailAddress = CodexJsonElementReader.ReadStringOrNull(rootElement, "email") ?? "",
            HttpStatusCode = httpStatusCode,
            RawResponseText = responseText,
            PrimaryWindow = CreateUsageWindow(rateLimitElement, "primary_window"),
            SecondaryWindow = CreateUsageWindow(rateLimitElement, "secondary_window")
        };
    }

    private static CodexUsageWindow CreateUsageWindow(JsonElement rateLimitElement, string propertyName)
    {
        if (!CodexJsonElementReader.TryGetProperty(rateLimitElement, propertyName, out var windowElement) || windowElement.ValueKind != JsonValueKind.Object) return new CodexUsageWindow();

        var usedPercentage = CodexJsonElementReader.ReadInt32OrNull(windowElement, "used_percent") ?? -1;
        return new CodexUsageWindow
        {
            UsedPercentage = usedPercentage,
            RemainingPercentage = usedPercentage is < 0 or > 100 ? -1 : 100 - usedPercentage,
            ResetAfterSeconds = CodexJsonElementReader.ReadInt64OrNull(windowElement, "reset_after_seconds") ?? -1,
            ResetAtUnixSeconds = CodexJsonElementReader.ReadInt64OrNull(windowElement, "reset_at") ?? -1
        };
    }
}
