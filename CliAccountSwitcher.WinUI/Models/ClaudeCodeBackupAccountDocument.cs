namespace CliAccountSwitcher.WinUI.Models;

public sealed class ClaudeCodeBackupAccountDocument
{
    public string CredentialsJson { get; set; } = "";

    public string GlobalConfigJson { get; set; } = "";

    public bool IsActive { get; set; }
}
