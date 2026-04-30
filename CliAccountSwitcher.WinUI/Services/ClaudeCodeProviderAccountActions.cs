using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ClaudeCodeProviderAccountActions(CliProviderAccountService cliProviderAccountService) : IProviderAccountActions
{
    public CliProviderKind ProviderKind => CliProviderKind.ClaudeCode;

    public string BackupFileNamePrefix => "claude-accounts";

    public async Task RefreshAccountsPageAllAsync(CancellationToken cancellationToken = default) => await cliProviderAccountService.RefreshAllClaudeCodeAccountsAsync(cancellationToken);

    public async Task RefreshAccountsPageSelectionAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default) => await cliProviderAccountService.RefreshClaudeCodeAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task RefreshAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default) => await cliProviderAccountService.RefreshClaudeCodeAccountsAsync([accountIdentifier], cancellationToken);

    public async Task DeleteAccountsAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default) => await cliProviderAccountService.DeleteClaudeCodeAccountsAsync(accountIdentifiers, cancellationToken);

    public async Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) => await cliProviderAccountService.ExportClaudeCodeBackupAsync(backupFilePath, cancellationToken);

    public async Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) => await cliProviderAccountService.ImportClaudeCodeBackupAsync(backupFilePath, cancellationToken);

    public async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default) => await cliProviderAccountService.DeleteExpiredClaudeCodeAccountsAsync(cancellationToken);

    public async Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default)
    {
        await cliProviderAccountService.ActivateClaudeCodeAccountAsync(accountIdentifier, cancellationToken);
        return ProviderActivationFollowUp.RefreshClaudeCodeSession;
    }
}
