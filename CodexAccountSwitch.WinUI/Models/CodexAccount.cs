using CodexAccountSwitch.Api.Models.Authentication;
using CodexAccountSwitch.Api.Models.Usage;
using System;
using System.Text.Json.Serialization;

namespace CodexAccountSwitch.WinUI.Models;

public sealed class CodexAccount
{
    public CodexAuthenticationDocument CodexAuthenticationDocument { get; set; } = new();

    public bool IsActive { get; set; }

    public string CustomAlias { get; set; } = "";

    public CodexUsageSnapshot LastCodexUsageSnapshot { get; set; } = new();

    public bool IsTokenExpired { get; set; }

    public DateTimeOffset? LastUsageRefreshTime { get; set; }

    [JsonIgnore]
    public string AccountIdentifier => CodexAuthenticationDocument.GetEffectiveAccountIdentifier();

    [JsonIgnore]
    public string EmailAddress => ResolveEmailAddress();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(CustomAlias) ? EmailAddress : CustomAlias;

    [JsonIgnore]
    public string PlanType => string.IsNullOrWhiteSpace(LastCodexUsageSnapshot.PlanType) ? CodexAuthenticationDocument.TryReadIdentityProfile()?.PlanType ?? "" : LastCodexUsageSnapshot.PlanType;

    public void MarkAsExpired() => IsTokenExpired = true;

    public void MarkAsValid() => IsTokenExpired = false;

    private string ResolveEmailAddress()
    {
        if (!string.IsNullOrWhiteSpace(LastCodexUsageSnapshot.EmailAddress)) return LastCodexUsageSnapshot.EmailAddress;
        if (!string.IsNullOrWhiteSpace(CodexAuthenticationDocument.EmailAddress)) return CodexAuthenticationDocument.EmailAddress;
        return CodexAuthenticationDocument.TryReadIdentityProfile()?.EmailAddress ?? "unknown";
    }
}
