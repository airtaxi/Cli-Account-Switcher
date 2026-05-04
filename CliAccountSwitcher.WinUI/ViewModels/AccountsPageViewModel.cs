using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class AccountsPageViewModel : ObservableObject, IDisposable
{
    private readonly CodexAccountService _codexAccountService;
    private readonly ApplicationSettings _applicationSettings;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly HashSet<string> _selectedAccountIdentifiers = new(StringComparer.Ordinal);
    private bool _isSynchronizingAccountSelection;
    private bool _disposed;

    public AccountsPageViewModel(CodexAccountService codexAccountService, ApplicationSettings applicationSettings, DispatcherQueue dispatcherQueue)
    {
        _codexAccountService = codexAccountService;
        _applicationSettings = applicationSettings;
        _dispatcherQueue = dispatcherQueue;
        SelectedProviderKind = _applicationSettings.SelectedProviderKind;
        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CodexAccount>>(this, OnCodexAccountChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CodexAccountStoreDocument>>(this, OnCodexAccountStoreDocumentChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadAccounts();
    }

    public ObservableCollection<CodexAccountViewModel> Accounts { get; } = [];

    public ObservableCollection<CodexAccountViewModel> FilteredAccounts { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedPlanFilter { get; set; } = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodexProviderSelected))]
    [NotifyPropertyChangedFor(nameof(IsClaudeCodeProviderSelected))]
    [NotifyPropertyChangedFor(nameof(PlanHeaderText))]
    public partial CliProviderKind SelectedProviderKind { get; set; } = CliProviderKind.Codex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccounts))]
    [NotifyPropertyChangedFor(nameof(HasNoAccounts))]
    [NotifyPropertyChangedFor(nameof(HasNoFilteredAccounts))]
    public partial int AccountCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredAccounts))]
    [NotifyPropertyChangedFor(nameof(HasNoFilteredAccounts))]
    public partial int FilteredAccountCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccounts))]
    [NotifyPropertyChangedFor(nameof(SelectedAccountCountText))]
    public partial IReadOnlyList<string> SelectedAccountIdentifiers { get; set; } = [];

    [ObservableProperty]
    public partial bool? FilteredAccountsSelectionState { get; set; } = false;

    public bool HasAccounts => AccountCount > 0;

    public bool HasNoAccounts => AccountCount == 0;

    public bool HasFilteredAccounts => FilteredAccountCount > 0;

    public bool HasNoFilteredAccounts => AccountCount > 0 && FilteredAccountCount == 0;

    public bool HasSelectedAccounts => SelectedAccountIdentifiers.Count > 0;

    public string SelectedAccountCountText => SelectedAccountIdentifiers.Count == 0 ? GetLocalizedString("AccountsPageViewModel_NoSelectedAccounts") : GetFormattedString("AccountsPageViewModel_SelectedAccountCountFormat", SelectedAccountIdentifiers.Count);

    public bool IsCodexProviderSelected => SelectedProviderKind == CliProviderKind.Codex;

    public bool IsClaudeCodeProviderSelected => SelectedProviderKind == CliProviderKind.ClaudeCode;

    public string PlanHeaderText => GetLocalizedString("AccountsPage_PlanHeaderTextBlock/Text");

    public void ReloadAccounts()
    {
        SelectedProviderKind = _applicationSettings.SelectedProviderKind;
        if (SelectedProviderKind == CliProviderKind.Codex)
        {
            SynchronizeAccounts(_codexAccountService.GetAccounts());
            ApplyFilter();
            RefreshAccountStateProperties();
            return;
        }

        _ = ReloadAccountsAsync();
    }

    public async Task ReloadAccountsAsync()
    {
        SelectedProviderKind = _applicationSettings.SelectedProviderKind;
        if (SelectedProviderKind == CliProviderKind.Codex)
        {
            ReloadAccounts();
            return;
        }

        try
        {
            var storedProviderAccounts = await App.CliProviderAccountService.GetClaudeCodeAccountsAsync();
            SynchronizeAccounts(storedProviderAccounts);
        }
        catch
        {
            SynchronizeAccounts(Array.Empty<StoredProviderAccount>());
        }

        ApplyFilter();
        RefreshAccountStateProperties();
    }

    public void SetSelectedAccountIdentifiers(IEnumerable<string> accountIdentifiers)
    {
        _selectedAccountIdentifiers.Clear();
        foreach (var accountIdentifier in accountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier))) _selectedAccountIdentifiers.Add(accountIdentifier);
        _isSynchronizingAccountSelection = true;
        try
        {
            foreach (var accountViewModel in Accounts) accountViewModel.IsSelected = _selectedAccountIdentifiers.Contains(accountViewModel.AccountIdentifier);
        }
        finally
        {
            _isSynchronizingAccountSelection = false;
        }
        SelectedAccountIdentifiers = [.. _selectedAccountIdentifiers];
        RefreshFilteredAccountsSelectionState();
    }

    public void SetFilteredAccountsSelection(bool isSelected)
    {
        _isSynchronizingAccountSelection = true;
        try
        {
            foreach (var accountViewModel in FilteredAccounts) accountViewModel.IsSelected = isSelected;
        }
        finally
        {
            _isSynchronizingAccountSelection = false;
        }

        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applicationSettings.PropertyChanged -= OnApplicationSettingsPropertyChanged;
        foreach (var accountViewModel in Accounts) accountViewModel.PropertyChanged -= OnAccountViewModelPropertyChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CodexAccount>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CodexAccountStoreDocument>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(ApplicationSettings.PrimaryUsageWarningThresholdPercentage) and not nameof(ApplicationSettings.SecondaryUsageWarningThresholdPercentage)) return;
        if (_dispatcherQueue.HasThreadAccess) RefreshUsageWarningThresholdProperties();
        else _dispatcherQueue.TryEnqueue(RefreshUsageWarningThresholdProperties);
    }

    private void OnCodexAccountChangedMessageReceived(object recipient, ValueChangedMessage<CodexAccount> valueChangedMessage)
    {
        if (SelectedProviderKind != CliProviderKind.Codex) return;
        if (_dispatcherQueue.HasThreadAccess) ApplyAccountChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyAccountChange(valueChangedMessage.Value));
    }

    private void OnCodexAccountStoreDocumentChangedMessageReceived(object recipient, ValueChangedMessage<CodexAccountStoreDocument> valueChangedMessage)
    {
        if (SelectedProviderKind != CliProviderKind.Codex) return;
        if (_dispatcherQueue.HasThreadAccess) ApplyAccountStoreDocumentChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyAccountStoreDocumentChange(valueChangedMessage.Value));
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (_dispatcherQueue.HasThreadAccess) ApplyProviderKindChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyProviderKindChange(valueChangedMessage.Value));
    }

    private void OnAccountViewModelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName != nameof(CodexAccountViewModel.IsSelected)) return;
        if (_isSynchronizingAccountSelection) return;
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private void ApplyAccountChange(CodexAccount codexAccount)
    {
        var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
        if (existingAccountViewModel is null) Accounts.Add(CreateAccountViewModel(codexAccount));
        else existingAccountViewModel.Update(codexAccount);

        SortAccounts();
        ApplyFilter();
        RefreshAccountStateProperties();
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private void ApplyAccountStoreDocumentChange(CodexAccountStoreDocument codexAccountStoreDocument)
    {
        SynchronizeAccounts(codexAccountStoreDocument.Accounts);
        ApplyFilter();
        RefreshAccountStateProperties();
    }

    private void SynchronizeAccounts(IReadOnlyList<CodexAccount> codexAccounts)
    {
        var accountIdentifiers = codexAccounts.Select(codexAccount => codexAccount.AccountIdentifier).ToHashSet(StringComparer.Ordinal);
        for (var accountIndex = Accounts.Count - 1; accountIndex >= 0; accountIndex--)
        {
            var accountViewModel = Accounts[accountIndex];
            if (accountIdentifiers.Contains(accountViewModel.AccountIdentifier)) continue;
            accountViewModel.PropertyChanged -= OnAccountViewModelPropertyChanged;
            _selectedAccountIdentifiers.Remove(accountViewModel.AccountIdentifier);
            Accounts.RemoveAt(accountIndex);
        }

        foreach (var codexAccount in codexAccounts)
        {
            var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
            if (existingAccountViewModel is null) Accounts.Add(CreateAccountViewModel(codexAccount));
            else existingAccountViewModel.Update(codexAccount);
        }

        SortAccounts();
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private void SynchronizeAccounts(IReadOnlyList<StoredProviderAccount> storedProviderAccounts)
    {
        var storedAccountIdentifiers = storedProviderAccounts.Select(storedProviderAccount => storedProviderAccount.StoredAccountIdentifier).ToHashSet(StringComparer.Ordinal);
        for (var accountIndex = Accounts.Count - 1; accountIndex >= 0; accountIndex--)
        {
            var accountViewModel = Accounts[accountIndex];
            if (accountViewModel.ProviderKind == CliProviderKind.ClaudeCode && storedAccountIdentifiers.Contains(accountViewModel.AccountIdentifier)) continue;
            accountViewModel.PropertyChanged -= OnAccountViewModelPropertyChanged;
            _selectedAccountIdentifiers.Remove(accountViewModel.AccountIdentifier);
            Accounts.RemoveAt(accountIndex);
        }

        foreach (var storedProviderAccount in storedProviderAccounts)
        {
            var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, storedProviderAccount.StoredAccountIdentifier, StringComparison.Ordinal));
            if (existingAccountViewModel is null) Accounts.Add(CreateAccountViewModel(storedProviderAccount));
            else
            {
                existingAccountViewModel.Update(storedProviderAccount);
                ApplyClaudeCodeUsageSnapshotCache(existingAccountViewModel);
            }
        }

        SortAccounts();
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private CodexAccountViewModel CreateAccountViewModel(CodexAccount codexAccount)
    {
        var accountViewModel = new CodexAccountViewModel(codexAccount, _applicationSettings)
        {
            IsSelected = _selectedAccountIdentifiers.Contains(codexAccount.AccountIdentifier)
        };
        accountViewModel.PropertyChanged += OnAccountViewModelPropertyChanged;
        return accountViewModel;
    }

    private CodexAccountViewModel CreateAccountViewModel(StoredProviderAccount storedProviderAccount)
    {
        var accountViewModel = new CodexAccountViewModel(storedProviderAccount, _applicationSettings)
        {
            IsSelected = _selectedAccountIdentifiers.Contains(storedProviderAccount.StoredAccountIdentifier)
        };
        ApplyClaudeCodeUsageSnapshotCache(accountViewModel);
        accountViewModel.PropertyChanged += OnAccountViewModelPropertyChanged;
        return accountViewModel;
    }

    private static void ApplyClaudeCodeUsageSnapshotCache(CodexAccountViewModel accountViewModel)
    {
        if (App.CliProviderAccountService.TryGetClaudeCodeUsageSnapshot(accountViewModel.AccountIdentifier, out var providerUsageSnapshot))
        {
            var usageRefreshTime = App.CliProviderAccountService.TryGetClaudeCodeUsageRefreshTime(accountViewModel.AccountIdentifier, out var cachedUsageRefreshTime) ? cachedUsageRefreshTime : DateTimeOffset.UtcNow;
            accountViewModel.UpdateProviderUsageSnapshot(providerUsageSnapshot, usageRefreshTime);
            return;
        }

        accountViewModel.ClearProviderUsageSnapshot();
    }

    private void SortAccounts()
    {
        var sortedAccountViewModels = Accounts.OrderByDescending(accountViewModel => accountViewModel.IsActive).ThenBy(accountViewModel => accountViewModel.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var accountIndex = 0; accountIndex < sortedAccountViewModels.Count; accountIndex++)
        {
            var accountViewModel = sortedAccountViewModels[accountIndex];
            var currentAccountIndex = Accounts.IndexOf(accountViewModel);
            if (currentAccountIndex != accountIndex) Accounts.Move(currentAccountIndex, accountIndex);
        }
    }

    private void ApplyFilter()
    {
        var normalizedSearchText = (SearchText ?? "").Trim();
        var normalizedSelectedPlanFilter = !string.IsNullOrWhiteSpace(SelectedPlanFilter) ? SelectedPlanFilter.Trim() : "All";
        var filteredAccountViewModels = Accounts.Where(accountViewModel => IsAccountVisible(accountViewModel, normalizedSearchText, normalizedSelectedPlanFilter)).ToList();
        var filteredAccountViewModelSet = filteredAccountViewModels.ToHashSet();

        for (var accountIndex = FilteredAccounts.Count - 1; accountIndex >= 0; accountIndex--)
        {
            if (!filteredAccountViewModelSet.Contains(FilteredAccounts[accountIndex])) FilteredAccounts.RemoveAt(accountIndex);
        }

        for (var accountIndex = 0; accountIndex < filteredAccountViewModels.Count; accountIndex++)
        {
            var accountViewModel = filteredAccountViewModels[accountIndex];
            var currentAccountIndex = FilteredAccounts.IndexOf(accountViewModel);
            if (currentAccountIndex < 0) FilteredAccounts.Insert(accountIndex, accountViewModel);
            else if (currentAccountIndex != accountIndex) FilteredAccounts.Move(currentAccountIndex, accountIndex);
        }
        RefreshAccountCounts();
        RefreshFilteredAccountsSelectionState();
    }

    private static bool IsAccountVisible(CodexAccountViewModel accountViewModel, string normalizedSearchText, string normalizedSelectedPlanFilter)
    {
        var matchesSearch = string.IsNullOrWhiteSpace(normalizedSearchText) || accountViewModel.SearchText.Contains(normalizedSearchText, StringComparison.CurrentCultureIgnoreCase);
        if (!matchesSearch) return false;
        if (string.Equals(normalizedSelectedPlanFilter, "All", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(accountViewModel.PlanFilterKey, normalizedSelectedPlanFilter.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshAccountStateProperties() => RefreshAccountCounts();

    private void RefreshSelectedAccountIdentifiersFromAccountViewModels()
    {
        _selectedAccountIdentifiers.Clear();
        foreach (var accountViewModel in Accounts.Where(accountViewModel => accountViewModel.IsSelected)) _selectedAccountIdentifiers.Add(accountViewModel.AccountIdentifier);
        SelectedAccountIdentifiers = [.. _selectedAccountIdentifiers];
        RefreshFilteredAccountsSelectionState();
    }

    private void RefreshFilteredAccountsSelectionState()
    {
        if (FilteredAccounts.Count == 0)
        {
            FilteredAccountsSelectionState = false;
            return;
        }

        var selectedFilteredAccountCount = FilteredAccounts.Count(accountViewModel => accountViewModel.IsSelected);
        FilteredAccountsSelectionState = selectedFilteredAccountCount == 0 ? false : selectedFilteredAccountCount == FilteredAccounts.Count ? true : null;
    }

    private void RefreshUsageWarningThresholdProperties()
    {
        foreach (var accountViewModel in Accounts) accountViewModel.RefreshUsageWarningThresholdProperties();
    }

    private void RefreshAccountCounts()
    {
        AccountCount = Accounts.Count;
        FilteredAccountCount = FilteredAccounts.Count;
    }

    private void ApplyProviderKindChange(CliProviderKind providerKind)
    {
        SelectedProviderKind = providerKind;
        SelectedPlanFilter = "All";
        SetSelectedAccountIdentifiers([]);
        _ = ReloadAccountsAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedPlanFilterChanged(string value) => ApplyFilter();

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
