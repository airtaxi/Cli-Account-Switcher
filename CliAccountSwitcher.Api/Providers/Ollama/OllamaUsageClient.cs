using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CliAccountSwitcher.Api.Providers.Ollama.Models;
using CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;
using HtmlAgilityPack;

namespace CliAccountSwitcher.Api.Providers.Ollama;

public sealed class OllamaUsageClient(HttpClient httpClient)
{
    private const string SessionUsageLabel = "Session usage";
    private const string WeeklyUsageLabel = "Weekly usage";

    private static readonly Regex s_percentUsedPattern = new(@"([0-9]+(?:\.[0-9]+)?)\s*%\s*used", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_barWidthPattern = new(@"width:\s*([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_durationTokenPattern = new(@"(\d+)\s*(week|day|hour|hr|minute|min)s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] s_signedOutMarkers =
    [
        "sign in to ollama",
        "log in to ollama",
        "/api/auth/signin",
        "href=\"/signin\"",
        "action=\"/signin\""
    ];

    private static readonly Dictionary<string, string> s_durationUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["week"] = "weeks",
        ["day"] = "days",
        ["hour"] = "hours",
        ["hr"] = "hours",
        ["minute"] = "minutes",
        ["min"] = "minutes"
    };

    public static (string UserName, string EmailAddress) ParseUserIdentityFromHtml(string htmlText)
    {
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(htmlText);
        return ParseUserIdentity(htmlDocument);
    }

    public async Task<OllamaUsageSnapshot> GetUsageAsync(string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie)) throw new ArgumentException("The Ollama auth cookie is required.", nameof(authCookie));

        var requestUri = new Uri(OllamaApiConventions.SettingsBaseUri, OllamaApiConventions.SettingsPath);
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequestMessage.Headers.Add("Cookie", $"{OllamaApiConventions.AuthCookieName}={authCookie}");
        httpRequestMessage.Headers.Add("User-Agent", OllamaApiConventions.UserAgent);
        httpRequestMessage.Headers.Add("Accept", "text/html");

        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        if (httpResponseMessage.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new OllamaAuthExpiredException("The Ollama session cookie has been rejected.");

        httpResponseMessage.EnsureSuccessStatusCode();
        var responseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
        return ParseUsageHtml(responseText);
    }

    private static OllamaUsageSnapshot ParseUsageHtml(string htmlText)
    {
        var snapshot = new OllamaUsageSnapshot { RawResponseText = htmlText };

        if (LooksSignedOut(htmlText)) throw new OllamaAuthExpiredException("The Ollama session cookie has expired: the settings page is signed out.");

        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(htmlText);

        (snapshot.UserName, snapshot.EmailAddress) = ParseUserIdentity(htmlDocument);
        snapshot.PlanLevel = ParsePlanLevel(htmlDocument);
        snapshot.SessionUsage = ParseUsageWindow(htmlDocument, SessionUsageLabel);
        snapshot.WeeklyUsage = ParseUsageWindow(htmlDocument, WeeklyUsageLabel);

        return snapshot;
    }

    private static bool LooksSignedOut(string html)
    {
        var lower = html.ToLowerInvariant();
        foreach (var marker in s_signedOutMarkers) if (lower.Contains(marker)) return true;
        return false;
    }

    private static (string UserName, string EmailAddress) ParseUserIdentity(HtmlDocument htmlDocument)
    {
        var emailNode = htmlDocument.GetElementbyId("header-email");
        if (emailNode is null) return ("", "");

        var headerContainer = emailNode.ParentNode;
        var userName = "";
        var emailAddress = emailNode.InnerText.Trim();

        var anchorNode = headerContainer.SelectSingleNode(".//a");
        if (anchorNode is not null)
        {
            var href = anchorNode.GetAttributeValue("href", "");
            if (href.StartsWith('/') && href.Length > 1 && !href[1..].Contains('/')) userName = anchorNode.InnerText.Trim();
        }

        return (userName, emailAddress);
    }

    private static string ParsePlanLevel(HtmlDocument htmlDocument)
    {
        var capitalizeNode = htmlDocument.DocumentNode.SelectSingleNode("//span[contains(concat(' ', normalize-space(@class), ' '), ' capitalize ')]");
        if (capitalizeNode is null) return "";
        var planLevel = capitalizeNode.InnerText.Trim();
        return string.IsNullOrEmpty(planLevel) ? "" : char.ToUpperInvariant(planLevel[0]) + planLevel[1..];
    }

    private static OllamaUsageWindow ParseUsageWindow(HtmlDocument htmlDocument, string label)
    {
        var labelNode = FindSpanByText(htmlDocument, label);
        if (labelNode is null) return new OllamaUsageWindow();

        var sectionDiv = FindSectionDiv(labelNode);
        if (sectionDiv is null) return new OllamaUsageWindow();

        var percent = ParsePercentFromSection(sectionDiv, labelNode);
        if (percent is null) return new OllamaUsageWindow();

        var resetAt = ParseResetAtFromSection(sectionDiv);
        var usedPercentage = (int)Math.Round(percent.Value);
        return new OllamaUsageWindow
        {
            UsedPercentage = usedPercentage,
            RemainingPercentage = 100 - usedPercentage,
            ResetAt = resetAt,
            ResetAfterSeconds = resetAt is null ? -1 : (long)(resetAt.Value - DateTimeOffset.UtcNow).TotalSeconds
        };
    }

    private static HtmlNode? FindSectionDiv(HtmlNode labelNode)
    {
        foreach (var ancestor in labelNode.Ancestors("div"))
        {
            if (ancestor.SelectSingleNode(".//*[@data-usage-track]") is not null) return ancestor;
            if (ancestor.SelectSingleNode(".//*[@data-time]") is not null) return ancestor;
        }
        return labelNode.Ancestors("div").LastOrDefault();
    }

    private static HtmlNode? FindSpanByText(HtmlDocument htmlDocument, string text)
    {
        var spans = htmlDocument.DocumentNode.SelectNodes("//span");
        if (spans is null) return null;
        foreach (var span in spans) if (span.InnerText.Trim().Equals(text, StringComparison.OrdinalIgnoreCase)) return span;
        return null;
    }

    private static double? ParsePercentFromSection(HtmlNode sectionDiv, HtmlNode labelNode)
    {
        var siblingSpan = labelNode.NextSiblingElement("span") ?? labelNode.PreviousSiblingElement("span");
        if (siblingSpan is not null)
        {
            var match = s_percentUsedPattern.Match(siblingSpan.InnerText);
            if (match.Success) return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        var trackNode = sectionDiv.SelectSingleNode(".//*[@data-usage-track]");
        if (trackNode is not null)
        {
            var ariaLabel = trackNode.GetAttributeValue("aria-label", "");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                var match = s_percentUsedPattern.Match(ariaLabel);
                if (match.Success) return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        var styleWidthNode = sectionDiv.SelectSingleNode(".//div[contains(@style, 'width:')]");
        if (styleWidthNode is not null)
        {
            var style = styleWidthNode.GetAttributeValue("style", "");
            var match = s_barWidthPattern.Match(style);
            if (match.Success) return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static DateTimeOffset? ParseResetAtFromSection(HtmlNode sectionDiv)
    {
        var dataTimeNode = sectionDiv.SelectSingleNode(".//*[@data-time]");
        if (dataTimeNode is not null)
        {
            var raw = dataTimeNode.GetAttributeValue("data-time", "");
            if (!string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, null, DateTimeStyles.AdjustToUniversal, out var parsed)) return parsed;
        }

        return ParseRelativeReset(sectionDiv.InnerText);
    }

    private static DateTimeOffset? ParseRelativeReset(string text)
    {
        var resetIndex = text.IndexOf("resets in", StringComparison.OrdinalIgnoreCase);
        if (resetIndex == -1) return null;

        var phrase = text[resetIndex..];
        var tokens = s_durationTokenPattern.Matches(phrase);
        if (tokens.Count == 0) return null;

        var delta = TimeSpan.Zero;
        foreach (Match token in tokens)
        {
            var amount = int.Parse(token.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = token.Groups[2].Value;
            delta += unit.ToLowerInvariant() switch
            {
                "week" => TimeSpan.FromDays(7 * amount),
                "day" => TimeSpan.FromDays(amount),
                "hour" or "hr" => TimeSpan.FromHours(amount),
                "minute" or "min" => TimeSpan.FromMinutes(amount),
                _ => TimeSpan.Zero
            };
        }

        return delta == TimeSpan.Zero ? null : DateTimeOffset.UtcNow + delta;
    }
}

internal static class HtmlNodeExtensions
{
    public static HtmlNode? NextSiblingElement(this HtmlNode node, string name)
    {
        var sibling = node.NextSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == HtmlNodeType.Element && string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase)) return sibling;
            sibling = sibling.NextSibling;
        }
        return null;
    }

    public static HtmlNode? PreviousSiblingElement(this HtmlNode node, string name)
    {
        var sibling = node.PreviousSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == HtmlNodeType.Element && string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase)) return sibling;
            sibling = sibling.PreviousSibling;
        }
        return null;
    }
}