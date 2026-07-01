namespace CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;

public sealed class OllamaUsageWindow
{
    public int UsedPercentage { get; set; } = -1;

    public int RemainingPercentage { get; set; } = -1;

    public DateTimeOffset? ResetAt { get; set; }

    public long ResetAfterSeconds { get; set; } = -1;
}