namespace CliAccountSwitcher.WinUI.Models;

public sealed class ProviderAccountBackupImportResult
{
    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public int DuplicateCount { get; set; }
}
