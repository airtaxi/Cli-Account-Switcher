using System.Net;
using System.Text.RegularExpressions;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models.Usage;

namespace CliAccountSwitcher.Api.Providers.OpenCodeGo;

public sealed class OpenCodeGoUsageClient(HttpClient httpClient)
{
    private static readonly Regex s_rollingUsagePattern = new(@"rollingUsage:(?:\$R\[\d+\]=)?\{status:""([^""]*)"",resetInSec:(\d+),usagePercent:(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex s_weeklyUsagePattern = new(@"weeklyUsage:(?:\$R\[\d+\]=)?\{status:""([^""]*)"",resetInSec:(\d+),usagePercent:(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex s_monthlyUsagePattern = new(@"monthlyUsage:(?:\$R\[\d+\]=)?\{status:""([^""]*)"",resetInSec:(\d+),usagePercent:(\d+)\}", RegexOptions.Compiled);

    public async Task<OpenCodeGoUsageSnapshot> GetUsageAsync(string workspaceId, string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) throw new ArgumentException("The workspace ID is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(authCookie)) throw new ArgumentException("The auth cookie is required.", nameof(authCookie));

        var usagePagePath = string.Format(OpenCodeGoApiConventions.UsagePagePathTemplate, workspaceId);
        var requestUri = new Uri(OpenCodeGoApiConventions.ConsoleBaseUri, usagePagePath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Add("Cookie", $"{OpenCodeGoApiConventions.AuthCookieName}={authCookie}");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        if (httpResponseMessage.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new OpenCodeGoAuthExpiredException("The OpenCode Go auth cookie has expired.");

        httpResponseMessage.EnsureSuccessStatusCode();
        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        return ParseUsageHtml(responseText);
    }

    private static OpenCodeGoUsageSnapshot ParseUsageHtml(string htmlText)
    {
        var snapshot = new OpenCodeGoUsageSnapshot { RawResponseText = htmlText };

        snapshot.RollingUsage = ParseUsageWindow(htmlText, s_rollingUsagePattern);
        snapshot.WeeklyUsage = ParseUsageWindow(htmlText, s_weeklyUsagePattern);
        snapshot.MonthlyUsage = ParseUsageWindow(htmlText, s_monthlyUsagePattern);

        return snapshot;
    }

    private static OpenCodeGoUsageWindow ParseUsageWindow(string htmlText, Regex pattern)
    {
        var match = pattern.Match(htmlText);
        if (!match.Success) return new OpenCodeGoUsageWindow();

        var status = match.Groups[1].Value;
        if (!string.Equals(status, "ok", StringComparison.Ordinal) && !string.Equals(status, "rate-limited", StringComparison.Ordinal)) return new OpenCodeGoUsageWindow();

        var resetInSec = long.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var usagePercent = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var resetAt = DateTimeOffset.UtcNow.AddSeconds(resetInSec);

        return new OpenCodeGoUsageWindow
        {
            UsedPercentage = usagePercent,
            RemainingPercentage = 100 - usagePercent,
            ResetAfterSeconds = resetInSec,
            ResetAt = resetAt
        };
    }
}
