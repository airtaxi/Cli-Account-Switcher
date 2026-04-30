namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderUsageWindow
{
    public int UsedPercentage { get; set; } = -1;

    public int RemainingPercentage { get; set; } = -1;

    public long ResetAfterSeconds { get; set; } = -1;

    public DateTimeOffset? ResetAt { get; set; }
}
