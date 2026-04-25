namespace CodexAccountSwitch.WinUI.Models;

public sealed class CodexAccountBackupImportResult
{
    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public int DuplicateCount { get; set; }
}
