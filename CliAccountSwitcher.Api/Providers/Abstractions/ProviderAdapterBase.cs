namespace CliAccountSwitcher.Api.Providers.Abstractions;

public abstract class ProviderAdapterBase<TLiveAccountState, TStoredAccountPayload> : IProviderAdapter
{
    public abstract CliProviderKind ProviderKind { get; }

    public abstract string DisplayName { get; }

    public abstract ProviderCapabilities Capabilities { get; }

    public abstract string? GetDefaultInputFilePath();

    public abstract Task<ProviderIdentityProfile> GetCurrentIdentityAsync(CancellationToken cancellationToken = default);

    public abstract Task<string> NormalizeAuthenticationDocumentAsync(string authenticationDocumentText, CancellationToken cancellationToken = default);

    public abstract Task<ProviderLoginResult> RunLoginAsync(CancellationToken cancellationToken = default);

    public abstract Task<ProviderUsageSnapshot> GetUsageAsync(string? storedAccountIdentifier = null, CancellationToken cancellationToken = default);

    public abstract Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    public abstract Task<ProviderResponseResult> CreateResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<ProviderResponseStreamEvent> StreamResponseAsync(ProviderResponseRequest providerResponseRequest, CancellationToken cancellationToken = default);

    public async Task<IReadOnlyList<StoredProviderAccount>> ListStoredAccountsAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var currentIdentityProfile = await TryGetCurrentIdentityAsync(cancellationToken);

        return storedProviderAccounts
            .Select(storedProviderAccount => CloneStoredProviderAccount(storedProviderAccount, currentIdentityProfile is not null ? IsSameAccount(storedProviderAccount, currentIdentityProfile) : storedProviderAccount.IsActive))
            .ToArray();
    }

    public async Task<StoredProviderAccount> SaveCurrentAccountAsync(IProviderSnapshotStore providerSnapshotStore, ProviderStoredAccountSaveOptions? providerStoredAccountSaveOptions = null, CancellationToken cancellationToken = default)
    {
        var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        return await SaveLiveAccountStateAsync(providerSnapshotStore, liveAccountState, providerStoredAccountSaveOptions ?? new ProviderStoredAccountSaveOptions(), cancellationToken);
    }

    public async Task<StoredProviderAccount> SaveAccountAsync(IProviderSnapshotStore providerSnapshotStore, ProviderAccountDocumentSet providerAccountDocumentSet, ProviderStoredAccountSaveOptions? providerStoredAccountSaveOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerAccountDocumentSet);
        var liveAccountState = CreateLiveAccountState(providerAccountDocumentSet);
        return await SaveLiveAccountStateAsync(providerSnapshotStore, liveAccountState, providerStoredAccountSaveOptions ?? new ProviderStoredAccountSaveOptions(), cancellationToken);
    }

    public async Task<StoredProviderAccount> ActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        await BeforeActivateStoredAccountAsync(providerSnapshotStore, cancellationToken);

        var storedAccountPayloadContext = await LoadStoredAccountPayloadContextAsync(providerSnapshotStore, storedAccountIdentifier, cancellationToken);
        storedAccountPayloadContext.Payload = await PrepareStoredAccountPayloadForActivationAsync(providerSnapshotStore, storedAccountPayloadContext.StoredProviderAccount, storedAccountPayloadContext.Payload, cancellationToken);
        var liveAccountState = CreateLiveAccountState(storedAccountPayloadContext.Payload);

        await WriteLiveAccountStateForActivationAsync(providerSnapshotStore, storedAccountPayloadContext.StoredProviderAccount, liveAccountState, cancellationToken);
        await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        return CloneStoredProviderAccount(storedAccountPayloadContext.StoredProviderAccount, true);
    }

    public async Task<string> GetCurrentStoredAccountIdentifierAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken = default)
    {
        var currentIdentityProfile = await TryGetCurrentIdentityAsync(cancellationToken);
        if (currentIdentityProfile is null) return "";

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var currentStoredProviderAccount = storedProviderAccounts.FirstOrDefault(storedProviderAccount => IsSameAccount(storedProviderAccount, currentIdentityProfile));
        return currentStoredProviderAccount?.StoredAccountIdentifier ?? "";
    }

    public async Task<StoredProviderAccount?> UpdateStoredAccountFromCurrentLiveAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default)
    {
        var liveAccountState = await ReadLiveAccountStateAsync(cancellationToken);
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var storedProviderAccount = storedProviderAccounts.FirstOrDefault(candidateAccount => string.Equals(candidateAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
        if (storedProviderAccount is null || !IsSameAccount(storedProviderAccount, liveAccountState)) return null;

        var updatedStoredProviderAccount = CreateStoredProviderAccount(liveAccountState, storedProviderAccount.StoredAccountIdentifier, storedProviderAccount.SlotNumber, storedProviderAccount.IsActive);
        updatedStoredProviderAccount.IsTokenExpired = false;
        updatedStoredProviderAccount.LastProviderUsageSnapshot = storedProviderAccount.LastProviderUsageSnapshot;
        updatedStoredProviderAccount.LastUsageRefreshTime = storedProviderAccount.LastUsageRefreshTime;
        updatedStoredProviderAccount.LastUpdated = DateTimeOffset.UtcNow;

        var storedAccountPayload = CreateStoredAccountPayload(liveAccountState);
        await providerSnapshotStore.SaveAsync(updatedStoredProviderAccount, SerializeStoredAccountPayload(storedAccountPayload), cancellationToken);
        return updatedStoredProviderAccount;
    }

    public Task DeleteStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken = default) => providerSnapshotStore.DeleteAsync(ProviderKind, storedAccountIdentifier, cancellationToken);

    protected abstract Task<TLiveAccountState> ReadLiveAccountStateAsync(CancellationToken cancellationToken);

    protected abstract TLiveAccountState CreateLiveAccountState(ProviderAccountDocumentSet providerAccountDocumentSet);

    protected abstract TLiveAccountState CreateLiveAccountState(TStoredAccountPayload storedAccountPayload);

    protected abstract TStoredAccountPayload CreateStoredAccountPayload(TLiveAccountState liveAccountState);

    protected abstract string SerializeStoredAccountPayload(TStoredAccountPayload storedAccountPayload);

    protected abstract TStoredAccountPayload DeserializeStoredAccountPayload(string payloadJson, string storedAccountIdentifier);

    protected abstract StoredProviderAccount CreateStoredProviderAccount(TLiveAccountState liveAccountState, string storedAccountIdentifier, int slotNumber, bool isActive);

    protected abstract bool IsSameAccount(StoredProviderAccount storedProviderAccount, TLiveAccountState liveAccountState);

    protected abstract bool IsSameAccount(StoredProviderAccount storedProviderAccount, ProviderIdentityProfile providerIdentityProfile);

    protected abstract Task WriteLiveAccountStateAsync(TLiveAccountState liveAccountState, CancellationToken cancellationToken);

    protected virtual Task BeforeActivateStoredAccountAsync(IProviderSnapshotStore providerSnapshotStore, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task<TStoredAccountPayload> PrepareStoredAccountPayloadForActivationAsync(IProviderSnapshotStore providerSnapshotStore, StoredProviderAccount storedProviderAccount, TStoredAccountPayload storedAccountPayload, CancellationToken cancellationToken) => Task.FromResult(storedAccountPayload);

    protected virtual async Task WriteLiveAccountStateForActivationAsync(IProviderSnapshotStore providerSnapshotStore, StoredProviderAccount storedProviderAccount, TLiveAccountState liveAccountState, CancellationToken cancellationToken) => await WriteLiveAccountStateAsync(liveAccountState, cancellationToken);

    private async Task<StoredProviderAccount> SaveLiveAccountStateAsync(IProviderSnapshotStore providerSnapshotStore, TLiveAccountState liveAccountState, ProviderStoredAccountSaveOptions providerStoredAccountSaveOptions, CancellationToken cancellationToken)
    {
        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var existingStoredProviderAccount = storedProviderAccounts.FirstOrDefault(storedProviderAccount => IsSameAccount(storedProviderAccount, liveAccountState));
        var slotNumber = existingStoredProviderAccount?.SlotNumber ?? GetNextSlotNumber(storedProviderAccounts);
        var storedAccountIdentifier = existingStoredProviderAccount?.StoredAccountIdentifier ?? slotNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var storedProviderAccount = CreateStoredProviderAccount(liveAccountState, storedAccountIdentifier, slotNumber, providerStoredAccountSaveOptions.ShouldActivate);
        var storedAccountPayload = CreateStoredAccountPayload(liveAccountState);

        await providerSnapshotStore.SaveAsync(storedProviderAccount, SerializeStoredAccountPayload(storedAccountPayload), cancellationToken);
        if (providerStoredAccountSaveOptions.ShouldActivate) await providerSnapshotStore.SetActiveStoredAccountIdentifierAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        return storedProviderAccount;
    }

    private async Task<StoredAccountPayloadContext> LoadStoredAccountPayloadContextAsync(IProviderSnapshotStore providerSnapshotStore, string storedAccountIdentifier, CancellationToken cancellationToken)
    {
        var payloadJson = await providerSnapshotStore.GetPayloadJsonAsync(ProviderKind, storedAccountIdentifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ProviderActionRequiredException($"The stored {DisplayName} account slot was not found: {storedAccountIdentifier}");

        var storedProviderAccounts = await providerSnapshotStore.GetStoredAccountsAsync(ProviderKind, cancellationToken);
        var storedProviderAccount = storedProviderAccounts.FirstOrDefault(candidateAccount => string.Equals(candidateAccount.StoredAccountIdentifier, storedAccountIdentifier, StringComparison.OrdinalIgnoreCase));
        if (storedProviderAccount is null) throw new ProviderActionRequiredException($"The stored {DisplayName} account slot was not found: {storedAccountIdentifier}");

        return new StoredAccountPayloadContext
        {
            Payload = DeserializeStoredAccountPayload(payloadJson, storedAccountIdentifier),
            StoredProviderAccount = storedProviderAccount
        };
    }

    private async Task<ProviderIdentityProfile?> TryGetCurrentIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetCurrentIdentityAsync(cancellationToken);
        }
        catch { return null; }
    }

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
            PlanType = storedProviderAccount.PlanType,
            IsActive = isActive,
            IsTokenExpired = storedProviderAccount.IsTokenExpired,
            LastUpdated = storedProviderAccount.LastUpdated,
            LastProviderUsageSnapshot = storedProviderAccount.LastProviderUsageSnapshot,
            LastUsageRefreshTime = storedProviderAccount.LastUsageRefreshTime
        };

    private static int GetNextSlotNumber(IReadOnlyList<StoredProviderAccount> storedProviderAccounts) => storedProviderAccounts.Count == 0 ? 1 : storedProviderAccounts.Max(storedProviderAccount => storedProviderAccount.SlotNumber) + 1;

    private sealed class StoredAccountPayloadContext
    {
        public TStoredAccountPayload Payload { get; set; } = default!;

        public StoredProviderAccount StoredProviderAccount { get; set; } = new();
    }
}
