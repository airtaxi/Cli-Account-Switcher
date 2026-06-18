namespace CliAccountSwitcher.Api.Providers.Zai.Models.Usage;

public sealed class ZaiUsageWindow
{
    public int UsedPercentage { get; set; } = -1;

    public int RemainingPercentage { get; set; } = -1;

    public DateTimeOffset? ResetAt { get; set; }

    public long ResetAfterSeconds { get; set; } = -1;
}
