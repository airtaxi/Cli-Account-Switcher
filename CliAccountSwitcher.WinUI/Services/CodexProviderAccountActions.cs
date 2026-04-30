using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class CodexProviderAccountActions(CodexAccountService codexAccountService) : IProviderAccountActions
{
    public CliProviderKind ProviderKind => CliProviderKind.Codex;

    public string BackupFileNamePrefix => "codex-accounts";

    public async Task RefreshAccountsPageAllAsync(CancellationToken cancellationToken = default) => await codexAccountService.RefreshAllAccountsAsync(cancellationToken);

    public async Task RefreshAccountsPageSelectionAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default) => await codexAccountService.RefreshAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task RefreshAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default) => await codexAccountService.RefreshAccountsAsync([accountIdentifier], cancellationToken);

    public async Task DeleteAccountsAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default) => await codexAccountService.DeleteAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) => await codexAccountService.ExportBackupAsync(backupFilePath, cancellationToken);

    public async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var codexAccountBackupImportResult = await codexAccountService.ImportBackupAsync(backupFilePath, cancellationToken);
        return new ProviderAccountBackupImportResult
        {
            SuccessCount = codexAccountBackupImportResult.SuccessCount,
            FailureCount = codexAccountBackupImportResult.FailureCount,
            DuplicateCount = codexAccountBackupImportResult.DuplicateCount
        };
    }

    public async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default) => await codexAccountService.DeleteExpiredAccountsAsync(cancellationToken);

    public async Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default)
    {
        await codexAccountService.SwitchActiveAccountAsync(accountIdentifier, cancellationToken);
        return ProviderActivationFollowUp.RestartCodex;
    }
}
