namespace CliAccountSwitcher.Api.Providers.Claude;

internal sealed class ClaudeCodeProcessResult
{
    public int ExitCode { get; set; }

    public string OutputText { get; set; } = "";

    public string ErrorText { get; set; } = "";
}
