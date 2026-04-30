using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;

namespace CliAccountSwitcher.WinUI.Services;

public interface IProviderAccountActions
{
    CliProviderKind ProviderKind { get; }

    string BackupFileNamePrefix { get; }

    Task RefreshAccountsPageAllAsync(CancellationToken cancellationToken = default);

    Task RefreshAccountsPageSelectionAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default);

    Task RefreshAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default);

    Task DeleteAccountsAsync(IReadOnlyList<string> accountIdentifiers, CancellationToken cancellationToken = default);

    Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default);

    Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default);
}
