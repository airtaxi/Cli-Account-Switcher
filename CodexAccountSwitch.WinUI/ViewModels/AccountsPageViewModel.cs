using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace CodexAccountSwitch.WinUI.ViewModels;

public sealed partial class AccountsPageViewModel : ObservableObject, IDisposable
{
    private readonly CodexAccountService _codexAccountService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly HashSet<string> _selectedAccountIdentifiers = new(StringComparer.Ordinal);
    private bool _disposed;

    public AccountsPageViewModel(CodexAccountService codexAccountService, DispatcherQueue dispatcherQueue)
    {
        _codexAccountService = codexAccountService;
        _dispatcherQueue = dispatcherQueue;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CodexAccount>>(this, OnCodexAccountChangedMessageReceived);
        ReloadAccounts();
    }

    public ObservableCollection<CodexAccountViewModel> Accounts { get; } = [];

    public ObservableCollection<CodexAccountViewModel> FilteredAccounts { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedPlanFilter { get; set; } = "All";

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

    public bool HasAccounts => AccountCount > 0;

    public bool HasNoAccounts => AccountCount == 0;

    public bool HasFilteredAccounts => FilteredAccountCount > 0;

    public bool HasNoFilteredAccounts => AccountCount > 0 && FilteredAccountCount == 0;

    public bool HasSelectedAccounts => SelectedAccountIdentifiers.Count > 0;

    public string SelectedAccountCountText => SelectedAccountIdentifiers.Count == 0 ? GetLocalizedString("AccountsPageViewModel_NoSelectedAccounts") : GetFormattedString("AccountsPageViewModel_SelectedAccountCountFormat", SelectedAccountIdentifiers.Count);

    public void ReloadAccounts()
    {
        SynchronizeAccounts(_codexAccountService.GetAccounts());
        ApplyFilter();
        RefreshAccountStateProperties();
    }

    public void SetSelectedAccountIdentifiers(IEnumerable<string> accountIdentifiers)
    {
        _selectedAccountIdentifiers.Clear();
        foreach (var accountIdentifier in accountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier))) _selectedAccountIdentifiers.Add(accountIdentifier);
        SelectedAccountIdentifiers = [.. _selectedAccountIdentifiers];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CodexAccount>>(this);
    }

    private void OnCodexAccountChangedMessageReceived(object recipient, ValueChangedMessage<CodexAccount> valueChangedMessage)
    {
        if (_dispatcherQueue.HasThreadAccess) ApplyAccountChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyAccountChange(valueChangedMessage.Value));
    }

    private void ApplyAccountChange(CodexAccount codexAccount)
    {
        var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
        if (existingAccountViewModel is null) Accounts.Add(new CodexAccountViewModel(codexAccount));
        else existingAccountViewModel.Update(codexAccount);

        SortAccounts();
        ApplyFilter();
        RefreshAccountStateProperties();
    }

    private void SynchronizeAccounts(IReadOnlyList<CodexAccount> codexAccounts)
    {
        var accountIdentifiers = codexAccounts.Select(codexAccount => codexAccount.AccountIdentifier).ToHashSet(StringComparer.Ordinal);
        for (var accountIndex = Accounts.Count - 1; accountIndex >= 0; accountIndex--)
        {
            if (!accountIdentifiers.Contains(Accounts[accountIndex].AccountIdentifier)) Accounts.RemoveAt(accountIndex);
        }

        foreach (var codexAccount in codexAccounts)
        {
            var existingAccountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, codexAccount.AccountIdentifier, StringComparison.Ordinal));
            if (existingAccountViewModel is null) Accounts.Add(new CodexAccountViewModel(codexAccount));
            else existingAccountViewModel.Update(codexAccount);
        }

        SortAccounts();
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
        var normalizedSelectedPlanFilter = string.IsNullOrWhiteSpace(SelectedPlanFilter) ? "All" : SelectedPlanFilter.Trim();
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
    }

    private static bool IsAccountVisible(CodexAccountViewModel accountViewModel, string normalizedSearchText, string normalizedSelectedPlanFilter)
    {
        var matchesSearch = string.IsNullOrWhiteSpace(normalizedSearchText) || accountViewModel.SearchText.Contains(normalizedSearchText, StringComparison.CurrentCultureIgnoreCase);
        if (!matchesSearch) return false;
        if (string.Equals(normalizedSelectedPlanFilter, "All", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(accountViewModel.PlanFilterKey, normalizedSelectedPlanFilter.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshAccountStateProperties() => RefreshAccountCounts();

    private void RefreshAccountCounts()
    {
        AccountCount = Accounts.Count;
        FilteredAccountCount = FilteredAccounts.Count;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedPlanFilterChanged(string value) => ApplyFilter();

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
