namespace CliAccountSwitcher.WinUI.Models;

public sealed class ClaudeCodeBackupDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string ProviderKind { get; set; } = "ClaudeCode";

    public List<ClaudeCodeBackupAccountDocument> Accounts { get; set; } = [];
}
