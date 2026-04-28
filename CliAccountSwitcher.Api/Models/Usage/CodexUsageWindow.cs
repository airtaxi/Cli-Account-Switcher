namespace CliAccountSwitcher.Api.Models.Usage;

public sealed class CodexUsageWindow
{
    public int UsedPercentage { get; set; } = -1;

    public int RemainingPercentage { get; set; } = -1;

    public long ResetAfterSeconds { get; set; } = -1;

    public long ResetAtUnixSeconds { get; set; } = -1;
}
