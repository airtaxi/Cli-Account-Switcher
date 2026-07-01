using CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;
using System.Security.Cryptography;
using System.Text;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class OllamaAccount
{
    public string AuthCookie { get; set; } = "";

    public string UserName { get; set; } = "";

    public string EmailAddress { get; set; } = "";

    public bool IsActive { get; set; }

    public string CustomAlias { get; set; } = "";

    public OllamaUsageSnapshot LastOllamaUsageSnapshot { get; set; } = new();

    public bool IsTokenExpired { get; set; }

    public DateTimeOffset? LastUsageRefreshTime { get; set; }

    public DateTimeOffset? AuthCookieObtainedTime { get; set; }

    public string AccountIdentifier => ComputeAccountIdentifier(EmailAddress);

    public string PlanType => LastOllamaUsageSnapshot?.PlanLevel ?? "";

    public string DisplayName => string.IsNullOrWhiteSpace(CustomAlias) ? BuildDefaultDisplayName(UserName, EmailAddress) : CustomAlias;

    public void MarkAsExpired() => IsTokenExpired = true;

    public void MarkAsValid() => IsTokenExpired = false;

    public static string ComputeAccountIdentifier(string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return "";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(emailAddress));
        return Convert.ToHexStringLower(hashBytes)[..32];
    }

    private static string BuildDefaultDisplayName(string userName, string emailAddress)
    {
        if (!string.IsNullOrWhiteSpace(userName)) return userName;
        if (!string.IsNullOrWhiteSpace(emailAddress)) return emailAddress;
        return "Ollama";
    }
}