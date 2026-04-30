using System.Text;
using System.Text.Json;
using CliAccountSwitcher.Api.Providers;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Security;

namespace CliAccountSwitcher.Api.Storage;

public sealed class FileSystemProviderSnapshotStore : IProviderSnapshotStore
{
    private readonly string _rootDirectoryPath;
    private readonly WindowsDataProtectionService _windowsDataProtectionService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileSystemProviderSnapshotStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CliAccountSwitcher.Api.Test", "ProviderSnapshots"), new WindowsDataProtectionService())
    {
    }

    public FileSystemProviderSnapshotStore(string rootDirectoryPath, WindowsDataProtectionService windowsDataProtectionService)
    {
        _rootDirectoryPath = rootDirectoryPath;
        _windowsDataProtectionService = windowsDataProtectionService;
    }

    public async Task<IReadOnlyList<StoredProviderAccount>> GetStoredAccountsAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var providerKindText = providerKind.ToString();
            var manifestDocument = await ReadManifestDocumentAsync(cancellationToken);
            manifestDocument.ActiveStoredAccountIdentifiers.TryGetValue(providerKindText, out var activeStoredAccountIdentifier);

            return manifestDocument.Accounts
                .Where(storedProviderAccount => storedProviderAccount.ProviderKind == providerKind)
                .OrderBy(storedProviderAccount => storedProviderAccount.SlotNumber)
                .Select(storedProviderAccount => CloneStoredProviderAccount(storedProviderAccount, string.Equals(storedProviderAccount.StoredAccountIdentifier, activeStoredAccountIdentifier, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> GetPayloadJsonAsync(CliProviderKind providerKind, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var payloadFilePath = BuildPayloadFilePath(providerKind, storedAccountIdentifier);
            if (!File.Exists(payloadFilePath)) return null;

            var protectedBytes = await File.ReadAllBytesAsync(payloadFilePath, cancellationToken);
            var payloadBytes = _windowsDataProtectionService.Unprotect(protectedBytes, providerKind.ToString());
            return Encoding.UTF8.GetString(payloadBytes);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveAsync(StoredProviderAccount storedProviderAccount, string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedProviderAccount);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_rootDirectoryPath);
            Directory.CreateDirectory(BuildProviderDirectoryPath(storedProviderAccount.ProviderKind));

            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var protectedBytes = _windowsDataProtectionService.Protect(payloadBytes, storedProviderAccount.ProviderKind.ToString());
            await WriteBytesAtomicallyAsync(BuildPayloadFilePath(storedProviderAccount.ProviderKind, storedProviderAccount.StoredAccountIdentifier), protectedBytes, cancellationToken);

            var manifestDocument = await ReadManifestDocumentAsync(cancellationToken);
            var existingAccountIndex = manifestDocument.Accounts.FindIndex(candidateAccount => candidateAccount.ProviderKind == storedProviderAccount.ProviderKind && string.Equals(candidateAccount.StoredAccountIdentifier, storedProviderAccount.StoredAccountIdentifier, StringComparison.OrdinalIgnoreCase));
            var clonedStoredProviderAccount = CloneStoredProviderAccount(storedProviderAccount, storedProviderAccount.IsActive);
            if (existingAccountIndex >= 0) manifestDocument.Accounts[existingAccountIndex] = clonedStoredProviderAccount;
            else manifestDocument.Accounts.Add(clonedStoredProviderAccount);

            await WriteManifestDocumentAsync(manifestDocument, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteAsync(CliProviderKind providerKind, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var providerKindText = providerKind.ToString();
            var manifestDocument = await ReadManifestDocumentAsync(cancellationToken);
            manifestDocument.Accounts.RemoveAll(storedProviderAccount => storedProviderAccount.ProviderKind == providerKind && string.Equals(storedProviderAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
            if (manifestDocument.ActiveStoredAccountIdentifiers.TryGetValue(providerKindText, out var activeStoredAccountIdentifier) && string.Equals(activeStoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                manifestDocument.ActiveStoredAccountIdentifiers[providerKindText] = null;
            }

            await WriteManifestDocumentAsync(manifestDocument, cancellationToken);

            try
            {
                var payloadFilePath = BuildPayloadFilePath(providerKind, storedAccountIdentifier);
                if (File.Exists(payloadFilePath)) File.Delete(payloadFilePath);
            }
            catch { }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetActiveStoredAccountIdentifierAsync(CliProviderKind providerKind, string? storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var manifestDocument = await ReadManifestDocumentAsync(cancellationToken);
            manifestDocument.ActiveStoredAccountIdentifiers[providerKind.ToString()] = storedAccountIdentifier;
            await WriteManifestDocumentAsync(manifestDocument, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> GetActiveStoredAccountIdentifierAsync(CliProviderKind providerKind, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var manifestDocument = await ReadManifestDocumentAsync(cancellationToken);
            return manifestDocument.ActiveStoredAccountIdentifiers.TryGetValue(providerKind.ToString(), out var storedAccountIdentifier) ? storedAccountIdentifier : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<ProviderSnapshotManifestDocument> ReadManifestDocumentAsync(CancellationToken cancellationToken)
    {
        var manifestFilePath = BuildManifestFilePath();
        if (!File.Exists(manifestFilePath)) return new ProviderSnapshotManifestDocument();

        var manifestJson = await File.ReadAllTextAsync(manifestFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestJson)) return new ProviderSnapshotManifestDocument();

        var manifestDocument = JsonSerializer.Deserialize<ProviderSnapshotManifestDocument>(manifestJson, ProviderJsonSerializerOptions.Default) ?? new ProviderSnapshotManifestDocument();
        manifestDocument.Accounts ??= [];
        manifestDocument.ActiveStoredAccountIdentifiers ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return manifestDocument;
    }

    private async Task WriteManifestDocumentAsync(ProviderSnapshotManifestDocument manifestDocument, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectoryPath);
        var manifestJson = JsonSerializer.Serialize(manifestDocument, ProviderJsonSerializerOptions.Default);
        await WriteTextAtomicallyAsync(BuildManifestFilePath(), manifestJson, cancellationToken);
    }

    private string BuildManifestFilePath() => Path.Combine(_rootDirectoryPath, "manifest.json");

    private string BuildProviderDirectoryPath(CliProviderKind providerKind) => Path.Combine(_rootDirectoryPath, GetProviderDirectoryName(providerKind));

    private string BuildPayloadFilePath(CliProviderKind providerKind, string storedAccountIdentifier) => Path.Combine(BuildProviderDirectoryPath(providerKind), $"{storedAccountIdentifier}.bin");

    private static string GetProviderDirectoryName(CliProviderKind providerKind)
        => providerKind switch
        {
            CliProviderKind.Codex => "codex",
            CliProviderKind.ClaudeCode => "claude",
            _ => providerKind.ToString().ToLowerInvariant()
        };

    private static StoredProviderAccount CloneStoredProviderAccount(StoredProviderAccount storedProviderAccount, bool isActive)
        => new()
        {
            ProviderKind = storedProviderAccount.ProviderKind,
            StoredAccountIdentifier = storedProviderAccount.StoredAccountIdentifier,
            SlotNumber = storedProviderAccount.SlotNumber,
            EmailAddress = storedProviderAccount.EmailAddress,
            DisplayName = storedProviderAccount.DisplayName,
            AccountIdentifier = storedProviderAccount.AccountIdentifier,
            OrganizationIdentifier = storedProviderAccount.OrganizationIdentifier,
            OrganizationName = storedProviderAccount.OrganizationName,
            IsActive = isActive,
            IsTokenExpired = storedProviderAccount.IsTokenExpired,
            LastUpdated = storedProviderAccount.LastUpdated
        };

    private static async Task WriteTextAtomicallyAsync(string filePath, string fileText, CancellationToken cancellationToken)
    {
        var fileBytes = Encoding.UTF8.GetBytes(fileText);
        await WriteBytesAtomicallyAsync(filePath, fileBytes, cancellationToken);
    }

    private static async Task WriteBytesAtomicallyAsync(string filePath, byte[] fileBytes, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);

        var temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporaryFilePath, fileBytes, cancellationToken);
        File.Move(temporaryFilePath, filePath, true);
    }
}
