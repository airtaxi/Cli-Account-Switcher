namespace CliAccountSwitcher.Api.Providers.OpenCodeGo.Models.Usage;

public sealed class OpenCodeGoUsageWindow
{
    public int UsedPercentage { get; set; } = -1;

    public int RemainingPercentage { get; set; } = -1;

    public DateTimeOffset? ResetAt { get; set; }

    public long ResetAfterSeconds { get; set; } = -1;
}
