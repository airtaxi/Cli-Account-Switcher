using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class AccountsPageViewModel : ObservableObject, IDisposable
{
    private readonly AccountServiceManager _accountServiceManager;
    private readonly ApplicationSettings _applicationSettings;
    private readonly LocalizationService _localizationService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly HashSet<string> _selectedAccountIdentifiers = new(StringComparer.Ordinal);
    private bool _isSynchronizingAccountSelection;
    private bool _disposed;

    public AccountsPageViewModel(AccountServiceManager accountServiceManager, ApplicationSettings applicationSettings, LocalizationService localizationService, DispatcherQueue dispatcherQueue)
    {
        _accountServiceManager = accountServiceManager;
        _applicationSettings = applicationSettings;
        _localizationService = localizationService;
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
    [NotifyPropertyChangedFor(nameof(IsZaiProviderSelected))]
    [NotifyPropertyChangedFor(nameof(IsOpenCodeGoProviderSelected))]
    [NotifyPropertyChangedFor(nameof(DescriptionText))]
    [NotifyPropertyChangedFor(nameof(NoAccountsDescriptionText))]
    [NotifyPropertyChangedFor(nameof(PlanHeaderText))]
    [NotifyPropertyChangedFor(nameof(SearchBoxColumnSpan))]
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

    public string SelectedAccountCountText => SelectedAccountIdentifiers.Count == 0 ? _localizationService.GetLocalizedString("AccountsPageViewModel_NoSelectedAccounts") : _localizationService.GetFormattedString("AccountsPageViewModel_SelectedAccountCountFormat", SelectedAccountIdentifiers.Count);

    public bool IsCodexProviderSelected => SelectedProviderKind == CliProviderKind.Codex;

    public bool IsClaudeCodeProviderSelected => SelectedProviderKind == CliProviderKind.ClaudeCode;

    public bool IsZaiProviderSelected => SelectedProviderKind == CliProviderKind.Zai;

    public bool IsOpenCodeGoProviderSelected => SelectedProviderKind == CliProviderKind.OpenCodeGo;

    public int SearchBoxColumnSpan => IsOpenCodeGoProviderSelected ? 2 : 1;

    // Localizations
    public string DescriptionText => _localizationService.GetFormattedString("AccountsPageViewModel_DescriptionFormat", GetProviderDisplayName(SelectedProviderKind));
    public string NoAccountsDescriptionText => SelectedProviderKind switch { CliProviderKind.ClaudeCode => _localizationService.GetLocalizedString("AccountsPage_NoAccountsDescriptionTextBlock_ClaudeCode.Text"), CliProviderKind.Zai => _localizationService.GetLocalizedString("AccountsPage_NoAccountsDescriptionTextBlock_Zai.Text"), CliProviderKind.OpenCodeGo => _localizationService.GetLocalizedString("AccountsPage_NoAccountsDescriptionTextBlock_OpenCodeGo.Text"), _ => _localizationService.GetLocalizedString("AccountsPage_NoAccountsDescriptionTextBlock_Codex.Text") };
    public string PlanHeaderText => _localizationService.GetLocalizedString("AccountsPage_PlanHeaderTextBlock/Text");
    public string RefreshAllAccountsLoadingMessage => _localizationService.GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage");
    public string RefreshSelectedAccountsLoadingMessage => _localizationService.GetLocalizedString("AccountsPage_RefreshSelectedAccountsLoadingMessage");
    public string RefreshAccountLoadingMessage => _localizationService.GetLocalizedString("AccountsPage_RefreshAccountLoadingMessage");
    public string ImportBackupLoadingMessage => _localizationService.GetLocalizedString("AccountsPage_ImportBackupLoadingMessage");
    public string BackupFileExtension => GetBackupFileExtension(SelectedProviderKind);
    public string BackupFileTypeChoiceText => GetBackupFileTypeChoiceText(SelectedProviderKind);
    public string BackupSuggestedFileName => GetBackupSuggestedFileName(SelectedProviderKind);
    public string RenameAccountDialogTitle => _localizationService.GetLocalizedString("AccountsPage_RenameAccountDialogTitle");
    public string RenameAccountPlaceholderText => _localizationService.GetLocalizedString("AccountsPage_RenameAccountPlaceholderText");

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

    public async Task RefreshAllAccountsAsync()
    {
        var selectedProviderKind = SelectedProviderKind;
        await _accountServiceManager.RefreshAllAccountsAsync(selectedProviderKind);
        ReloadAccounts();
    }

    public async Task RefreshSelectedAccountsAsync()
    {
        var selectedProviderKind = SelectedProviderKind;
        var selectedAccountIdentifiers = SelectedAccountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier)).ToArray();
        if (selectedAccountIdentifiers.Length == 0) return;

        await _accountServiceManager.RefreshAccountsAsync(selectedProviderKind, selectedAccountIdentifiers);
        ReloadAccounts();
    }

    public async Task RefreshAccountAsync(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var selectedProviderKind = SelectedProviderKind;
        await _accountServiceManager.RefreshAccountAsync(selectedProviderKind, accountIdentifier);
        ReloadAccounts();
    }

    public async Task DeleteSelectedAccountsAsync()
    {
        var selectedProviderKind = SelectedProviderKind;
        var selectedAccountIdentifiers = SelectedAccountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier)).ToArray();
        if (selectedAccountIdentifiers.Length == 0) return;

        await _accountServiceManager.DeleteAccountsAsync(selectedProviderKind, selectedAccountIdentifiers);
        ReloadAccounts();
    }

    public async Task DeleteAccountAsync(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;

        var selectedProviderKind = SelectedProviderKind;
        await _accountServiceManager.DeleteAccountsAsync(selectedProviderKind, [accountIdentifier]);
        ReloadAccounts();
    }

    public async Task<ProviderActivationFollowUp> ActivateAccountAsync(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return ProviderActivationFollowUp.None;

        var selectedProviderKind = SelectedProviderKind;
        var providerActivationFollowUp = await _accountServiceManager.ActivateAccountAsync(selectedProviderKind, accountIdentifier);
        ReloadAccounts();
        return providerActivationFollowUp;
    }

    public bool TryGetAccountCustomAlias(string accountIdentifier, out string customAlias)
    {
        customAlias = "";
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return false;
        if (!_accountServiceManager.GetIsRenameSupported(SelectedProviderKind)) return false;

        var accountViewModel = Accounts.FirstOrDefault(accountViewModel => string.Equals(accountViewModel.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
        if (accountViewModel is null) return false;

        customAlias = accountViewModel.CustomAlias;
        return true;
    }

    public async Task RenameAccountAsync(string accountIdentifier, string customAlias)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier)) return;
        if (!_accountServiceManager.GetIsRenameSupported(SelectedProviderKind)) return;

        var selectedProviderKind = SelectedProviderKind;
        await _accountServiceManager.RenameAccountAsync(selectedProviderKind, accountIdentifier, customAlias);
        ReloadAccounts();
    }

    public string GetBackupFileTypeChoiceText(CliProviderKind providerKind) => _localizationService.GetLocalizedString(GetBackupFileTypeChoiceResourceName(providerKind));

    public string GetBackupSuggestedFileName(CliProviderKind providerKind) => $"{_accountServiceManager.GetBackupFileNamePrefix(providerKind)}-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

    public async Task<BasicDialogData> ExportBackupAsync(CliProviderKind providerKind, string backupFilePath)
    {
        await _accountServiceManager.ExportBackupAsync(providerKind, backupFilePath);
        return new BasicDialogData(_localizationService.GetLocalizedString("AccountsPage_ExportBackupDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_ExportBackupDialogMessage"));
    }

    public async Task<BasicDialogData> ImportBackupAsync(CliProviderKind providerKind, string backupFilePath)
    {
        var providerAccountBackupImportResult = default(ProviderAccountBackupImportResult);
        try
        {
            providerAccountBackupImportResult = await _accountServiceManager.ImportBackupAsync(providerKind, backupFilePath);
            ReloadAccounts();
        }
        catch { providerAccountBackupImportResult = new ProviderAccountBackupImportResult { FailureCount = 1 }; }

        return new BasicDialogData(_localizationService.GetLocalizedString("AccountsPage_ImportBackupDialogTitle"), BuildBackupImportResultText(providerAccountBackupImportResult.SuccessCount, providerAccountBackupImportResult.FailureCount, providerAccountBackupImportResult.DuplicateCount));
    }

    public async Task<BasicDialogData> DeleteExpiredAccountsAsync()
    {
        var selectedProviderKind = SelectedProviderKind;
        var deletedCount = await _accountServiceManager.DeleteExpiredAccountsAsync(selectedProviderKind);
        ReloadAccounts();
        var dialogMessage = deletedCount == 0 ? _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : _localizationService.GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedCount);
        return new BasicDialogData(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), dialogMessage);
    }

    public BasicDialogData CreateDeleteSelectedAccountsConfirmationDialogData() => new(_localizationService.GetLocalizedString("AccountsPage_DeleteSelectedAccountsDialogTitle"), _localizationService.GetFormattedString("AccountsPage_DeleteSelectedAccountsDialogMessage", SelectedAccountIdentifiers.Count), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));

    public BasicDialogData CreateDeleteAccountConfirmationDialogData() => new(_localizationService.GetLocalizedString("AccountsPage_DeleteAccountDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteAccountDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));

    public BasicDialogData CreateDeleteExpiredAccountsConfirmationDialogData() => new(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));

    public void SetSelectedAccountIdentifiers(IEnumerable<string> accountIdentifiers)
    {
        _selectedAccountIdentifiers.Clear();
        foreach (var accountIdentifier in accountIdentifiers.Where(accountIdentifier => !string.IsNullOrWhiteSpace(accountIdentifier))) _selectedAccountIdentifiers.Add(accountIdentifier);
        _isSynchronizingAccountSelection = true;
        try
        {
            foreach (var accountViewModel in Accounts)
            {
                accountViewModel.IsSelected = _selectedAccountIdentifiers.Contains(accountViewModel.AccountIdentifier);
            }
        }
        finally { _isSynchronizingAccountSelection = false; }
        SelectedAccountIdentifiers = [.. _selectedAccountIdentifiers];
        RefreshFilteredAccountsSelectionState();
    }

    public void SetFilteredAccountsSelection(bool isSelected)
    {
        _isSynchronizingAccountSelection = true;
        try
        {
            foreach (var accountViewModel in FilteredAccounts)
            {
                accountViewModel.IsSelected = isSelected;
            }
        }
        finally { _isSynchronizingAccountSelection = false; }

        RefreshSelectedAccountIdentifiersFromAccountViewModels();
    }

    public void RefreshUsageResetTextProperties()
    {
        foreach (var accountViewModel in Accounts)
        {
            accountViewModel.RefreshUsageResetTextProperties();
        }
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
        var accountViewModel = new ProviderAccountViewModel(providerAccount, _applicationSettings, _localizationService)
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
            if (!filteredAccountViewModelSet.Contains(FilteredAccounts[accountIndex]))
            {
                FilteredAccounts.RemoveAt(accountIndex);
            }
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
        foreach (var accountViewModel in Accounts)
        {
            accountViewModel.RefreshUsageWarningThresholdProperties();
        }
    }

    private void RefreshAccountCounts()
    {
        AccountCount = Accounts.Count;
        FilteredAccountCount = FilteredAccounts.Count;
    }

    private string BuildBackupImportResultText(int successCount, int failureCount, int duplicateCount)
    {
        var resultLines = new[]
        {
            successCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupSuccessCountFormat", successCount) : "",
            failureCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupFailureCountFormat", failureCount) : "",
            duplicateCount > 0 ? _localizationService.GetFormattedString("AccountsPage_ImportBackupDuplicateCountFormat", duplicateCount) : ""
        }.Where(resultLine => !string.IsNullOrWhiteSpace(resultLine)).ToArray();

        return resultLines.Length == 0 ? _localizationService.GetLocalizedString("AccountsPage_ImportBackupEmptyResultMessage") : string.Join(Environment.NewLine, resultLines);
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

    private string GetProviderDisplayName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => _localizationService.GetLocalizedString("Provider_ClaudeCodeDisplayName"), CliProviderKind.Zai => _localizationService.GetLocalizedString("Provider_ZaiDisplayName"), CliProviderKind.OpenCodeGo => _localizationService.GetLocalizedString("Provider_OpenCodeGoDisplayName"), _ => _localizationService.GetLocalizedString("Provider_CodexDisplayName") };

    public static string GetBackupFileExtension(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => ".ccb", CliProviderKind.Zai => ".zaib", CliProviderKind.OpenCodeGo => ".ocb", _ => ".zip" };

    private static string GetBackupFileTypeChoiceResourceName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => "AccountsPage_ClaudeCodeBackupFileTypeChoice", CliProviderKind.Zai => "AccountsPage_ZaiBackupFileTypeChoice", CliProviderKind.OpenCodeGo => "AccountsPage_OpenCodeGoBackupFileTypeChoice", _ => "AccountsPage_ZipBackupFileTypeChoice" };


}
