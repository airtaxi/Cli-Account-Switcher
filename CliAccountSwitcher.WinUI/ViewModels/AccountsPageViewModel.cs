using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class AccountsPageViewModel : ObservableObject, IDisposable
{
    private readonly AccountServiceManager _accountServiceManager;
    private readonly ApplicationSettings _applicationSettings;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly HashSet<string> _selectedAccountIdentifiers = new(StringComparer.Ordinal);
    private bool _isSynchronizingAccountSelection;
    private bool _disposed;

    public AccountsPageViewModel(AccountServiceManager accountServiceManager, ApplicationSettings applicationSettings, DispatcherQueue dispatcherQueue)
    {
        _accountServiceManager = accountServiceManager;
        _applicationSettings = applicationSettings;
        _dispatcherQueue = dispatcherQueue;
        SelectedProviderKind = _applicationSettings.SelectedProviderKind;
        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<ProviderAccountsChangedMessage>>(this, OnProviderAccountsChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadAccounts();
    }

    public ObservableCollection<ProviderAccountViewModel> Accounts { get; } = [];

    public ObservableCollection<ProviderAccountViewModel> FilteredAccounts { get; } = [];

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
        SynchronizeAccounts(_accountServiceManager.GetAccounts(SelectedProviderKind));
        ApplyFilter();
        RefreshAccountStateProperties();
    }

    public Task ReloadAccountsAsync()
    {
        ReloadAccounts();
        return Task.CompletedTask;
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
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<ProviderAccountsChangedMessage>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(ApplicationSettings.PrimaryUsageWarningThresholdPercentage) and not nameof(ApplicationSettings.SecondaryUsageWarningThresholdPercentage)) return;
        if (_dispatcherQueue.HasThreadAccess) RefreshUsageWarningThresholdProperties();
        else _dispatcherQueue.TryEnqueue(RefreshUsageWarningThresholdProperties);
    }

    private void OnProviderAccountsChangedMessageReceived(object recipient, ValueChangedMessage<ProviderAccountsChangedMessage> valueChangedMessage)
    {
        if (SelectedProviderKind != valueChangedMessage.Value.ProviderKind) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadAccounts();
        else _dispatcherQueue.TryEnqueue(ReloadAccounts);
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (_dispatcherQueue.HasThreadAccess) ApplyProviderKindChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyProviderKindChange(valueChangedMessage.Value));
    }

    private void OnAccountViewModelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName != nameof(ProviderAccountViewModel.IsSelected)) return;
        if (_isSynchronizingAccountSelection) return;
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private void SynchronizeAccounts(IReadOnlyList<ProviderAccount> providerAccounts)
    {
        var accountIdentifiers = providerAccounts.Select(providerAccount => providerAccount.AccountIdentifier).ToHashSet(StringComparer.Ordinal);
        for (var accountIndex = Accounts.Count - 1; accountIndex >= 0; accountIndex--)
        {
            var accountViewModel = Accounts[accountIndex];
            if (accountIdentifiers.Contains(accountViewModel.AccountIdentifier)) continue;
            accountViewModel.PropertyChanged -= OnAccountViewModelPropertyChanged;
            _selectedAccountIdentifiers.Remove(accountViewModel.AccountIdentifier);
            Accounts.RemoveAt(accountIndex);
        }

        foreach (var providerAccount in providerAccounts)
        {
            var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, providerAccount.AccountIdentifier, StringComparison.Ordinal));
            if (existingAccountViewModel is null) Accounts.Add(CreateAccountViewModel(providerAccount));
            else existingAccountViewModel.Update(providerAccount);
        }

        SortAccounts();
        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    private ProviderAccountViewModel CreateAccountViewModel(ProviderAccount providerAccount)
    {
        var accountViewModel = new ProviderAccountViewModel(providerAccount, _applicationSettings)
        {
            IsSelected = _selectedAccountIdentifiers.Contains(providerAccount.AccountIdentifier)
        };
        accountViewModel.PropertyChanged += OnAccountViewModelPropertyChanged;
        return accountViewModel;
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

    private static bool IsAccountVisible(ProviderAccountViewModel accountViewModel, string normalizedSearchText, string normalizedSelectedPlanFilter)
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
        ReloadAccounts();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedPlanFilterChanged(string value) => ApplyFilter();

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
