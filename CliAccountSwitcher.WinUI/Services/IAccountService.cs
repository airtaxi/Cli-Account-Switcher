using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;

namespace CliAccountSwitcher.WinUI.Services;

public interface IAccountService : IDisposable
{
    CliProviderKind ProviderKind { get; }

    string BackupFileNamePrefix { get; }

    bool IsRenameSupported { get; }

    event EventHandler AccountsChanged;

    IReadOnlyList<ProviderAccount> GetAccounts();

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SynchronizeActiveStatusesAsync(CancellationToken cancellationToken = default);

    Task RefreshAllAccountsAsync(CancellationToken cancellationToken = default);

    Task RefreshAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default);

    Task RefreshAccountsByActiveStateAsync(bool isActive, CancellationToken cancellationToken = default);

    Task RefreshActiveAccountAsync(CancellationToken cancellationToken = default);

    Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier, CancellationToken cancellationToken = default);

    Task DeleteAccountsAsync(IEnumerable<string> accountIdentifiers, CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken = default);

    Task RenameAccountAsync(string accountIdentifier, string customAlias, CancellationToken cancellationToken = default);

    Task ExportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    Task<ProviderAccountBackupImportResult> ImportBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
}
