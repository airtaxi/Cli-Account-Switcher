using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class DashboardPageViewModel : ObservableObject, IDisposable
{
    private readonly AccountServiceManager _accountServiceManager;
    private readonly ApplicationSettings _applicationSettings;
    private readonly LocalizationService _localizationService;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _disposed;

    public DashboardPageViewModel(AccountServiceManager accountServiceManager, ApplicationSettings applicationSettings, LocalizationService localizationService, DispatcherQueue dispatcherQueue)
    {
        _accountServiceManager = accountServiceManager;
        _applicationSettings = applicationSettings;
        _localizationService = localizationService;
        _dispatcherQueue = dispatcherQueue;
        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<ProviderAccountsChangedMessage>>(this, OnProviderAccountsChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadDashboard();
    }

    public ObservableCollection<DashboardLowUsageAccountViewModel> LowUsageAccounts { get; } = [];

    [ObservableProperty]
    public partial string AccountCountText { get; set; } = "";

    [ObservableProperty]
    public partial string TotalAccountDescriptionText { get; set; } = "";

    [ObservableProperty]
    public partial string PrimaryAverageUsageRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial int PrimaryAverageUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial string PrimaryLowUsageAccountCountText { get; set; } = "";

    [ObservableProperty]
    public partial string SecondaryAverageUsageRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial int SecondaryAverageUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial string SecondaryLowUsageAccountCountText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasActiveAccount { get; set; }

    [ObservableProperty]
    public partial bool HasNoActiveAccount { get; set; } = true;

    [ObservableProperty]
    public partial string ActiveAccountDisplayNameText { get; set; } = "";

    [ObservableProperty]
    public partial string ActiveAccountEmailAddressText { get; set; } = "";

    [ObservableProperty]
    public partial string ActiveAccountPlanText { get; set; } = "";

    [ObservableProperty]
    public partial string ActiveAccountPrimaryUsageRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial string ActiveAccountSecondaryUsageRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial int ActiveAccountPrimaryUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial int ActiveAccountSecondaryUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? ActiveAccountPrimaryUsageResetAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? ActiveAccountSecondaryUsageResetAt { get; set; }

    [ObservableProperty]
    public partial string ActiveAccountLastUsageRefreshText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActiveAccountPrimaryUsageUnderWarningThreshold { get; set; }

    [ObservableProperty]
    public partial bool IsActiveAccountSecondaryUsageUnderWarningThreshold { get; set; }

    [ObservableProperty]
    public partial bool IsActiveAccountPrimaryUsageOverAverageRateLimit { get; set; }

    [ObservableProperty]
    public partial bool IsActiveAccountSecondaryUsageOverAverageRateLimit { get; set; }

    [ObservableProperty]
    public partial int ActiveAccountPrimaryUsageAverageRateLimitExceededPercentage { get; set; }

    [ObservableProperty]
    public partial int ActiveAccountSecondaryUsageAverageRateLimitExceededPercentage { get; set; }

    [ObservableProperty]
    public partial int ActiveAccountPrimaryUsageAverageRateLimitHeadroomPercentage { get; set; }

    [ObservableProperty]
    public partial int ActiveAccountSecondaryUsageAverageRateLimitHeadroomPercentage { get; set; }

    [ObservableProperty]
    public partial bool HasLowUsageAccounts { get; set; }

    [ObservableProperty]
    public partial bool HasNoLowUsageAccounts { get; set; } = true;

    public bool IsActiveAccountEmailAddressVisible => _applicationSettings.SelectedProviderKind != CliProviderKind.Zai;

    public string RefreshAllAccountsLoadingMessage => _localizationService.GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage");

    public void ReloadDashboard()
    {
        var providerAccounts = _accountServiceManager.GetAccounts(_applicationSettings.SelectedProviderKind);
        var accountViewModels = providerAccounts.Select(providerAccount => new ProviderAccountViewModel(providerAccount, _applicationSettings, _localizationService)).ToList();

        AccountCountText = _localizationService.GetFormattedString("DashboardPageViewModel_AccountCountFormat", accountViewModels.Count);
        TotalAccountDescriptionText = _localizationService.GetFormattedString("DashboardPageViewModel_TotalAccountDescriptionFormat", GetProviderDisplayName(_applicationSettings.SelectedProviderKind));
        SetAverageUsageProperties(accountViewModels);
        SetLowUsageSummaryProperties(accountViewModels);
        SetActiveAccountProperties(accountViewModels.FirstOrDefault(accountViewModel => accountViewModel.IsActive));
        SetLowUsageAccounts(accountViewModels);
    }

    public Task ReloadDashboardAsync()
    {
        ReloadDashboard();
        return Task.CompletedTask;
    }

    public async Task RefreshAllAccountsAsync()
    {
        await _accountServiceManager.RefreshAllAccountsAsync(_applicationSettings.SelectedProviderKind);
        ReloadDashboard();
    }

    public async Task<BasicDialogData> DeleteExpiredAccountsAsync()
    {
        var deletedAccountCount = await _accountServiceManager.DeleteExpiredAccountsAsync(_applicationSettings.SelectedProviderKind);
        ReloadDashboard();
        var dialogMessage = deletedAccountCount == 0 ? _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : _localizationService.GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedAccountCount);
        return new BasicDialogData(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), dialogMessage);
    }

    public BasicDialogData CreateDeleteExpiredAccountsConfirmationDialogData() => new(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));

    public async Task RefreshActiveProviderAccountAsync()
    {
        await _accountServiceManager.RefreshActiveAccountAsync(_applicationSettings.SelectedProviderKind);
        ReloadDashboard();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applicationSettings.PropertyChanged -= OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<ProviderAccountsChangedMessage>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(ApplicationSettings.PrimaryUsageWarningThresholdPercentage) and not nameof(ApplicationSettings.SecondaryUsageWarningThresholdPercentage)) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void OnProviderAccountsChangedMessageReceived(object recipient, ValueChangedMessage<ProviderAccountsChangedMessage> valueChangedMessage)
    {
        if (_applicationSettings.SelectedProviderKind != valueChangedMessage.Value.ProviderKind) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage) => QueueReloadDashboard();

    private void QueueReloadDashboard()
    {
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void SetAverageUsageProperties(IReadOnlyList<ProviderAccountViewModel> accountViewModels)
    {
        var primaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(accountViewModels, accountViewModel => accountViewModel.ProviderUsageSnapshot.FiveHour.RemainingPercentage);
        var secondaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(accountViewModels, accountViewModel => accountViewModel.ProviderUsageSnapshot.SevenDay.RemainingPercentage);

        PrimaryAverageUsageRemainingText = FormatUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        PrimaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingText = FormatUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
    }

    private void SetLowUsageSummaryProperties(IReadOnlyList<ProviderAccountViewModel> accountViewModels)
    {
        var primaryLowUsageAccountCount = accountViewModels.Count(accountViewModel => accountViewModel.IsPrimaryUsageUnderWarningThreshold);
        var secondaryLowUsageAccountCount = accountViewModels.Count(accountViewModel => accountViewModel.IsSecondaryUsageUnderWarningThreshold);

        PrimaryLowUsageAccountCountText = FormatLowUsageAccountCount(primaryLowUsageAccountCount);
        SecondaryLowUsageAccountCountText = FormatLowUsageAccountCount(secondaryLowUsageAccountCount);
    }

    private void SetActiveAccountProperties(ProviderAccountViewModel activeAccountViewModel)
    {
        HasActiveAccount = activeAccountViewModel is not null;
        HasNoActiveAccount = activeAccountViewModel is null;

        ActiveAccountDisplayNameText = activeAccountViewModel?.DisplayName ?? "";
        ActiveAccountEmailAddressText = activeAccountViewModel?.EmailAddress ?? "";
        ActiveAccountPlanText = activeAccountViewModel?.PlanText ?? "";
        ActiveAccountPrimaryUsageRemainingText = activeAccountViewModel?.PrimaryUsageRemainingText ?? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownUsage");
        ActiveAccountSecondaryUsageRemainingText = activeAccountViewModel?.SecondaryUsageRemainingText ?? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownUsage");
        ActiveAccountPrimaryUsageRemainingPercentage = activeAccountViewModel?.PrimaryUsageRemainingPercentage ?? 0;
        ActiveAccountSecondaryUsageRemainingPercentage = activeAccountViewModel?.SecondaryUsageRemainingPercentage ?? 0;
        ActiveAccountPrimaryUsageResetAt = activeAccountViewModel is null ? null : GetUsageResetAt(activeAccountViewModel.ProviderUsageSnapshot.FiveHour);
        ActiveAccountSecondaryUsageResetAt = activeAccountViewModel is null ? null : GetUsageResetAt(activeAccountViewModel.ProviderUsageSnapshot.SevenDay);
        ActiveAccountLastUsageRefreshText = activeAccountViewModel?.LastUsageRefreshText ?? "";
        IsActiveAccountPrimaryUsageUnderWarningThreshold = activeAccountViewModel?.IsPrimaryUsageUnderWarningThreshold == true;
        IsActiveAccountSecondaryUsageUnderWarningThreshold = activeAccountViewModel?.IsSecondaryUsageUnderWarningThreshold == true;
        IsActiveAccountPrimaryUsageOverAverageRateLimit = activeAccountViewModel?.IsPrimaryUsageOverAverageRateLimit == true;
        IsActiveAccountSecondaryUsageOverAverageRateLimit = activeAccountViewModel?.IsSecondaryUsageOverAverageRateLimit == true;
        ActiveAccountPrimaryUsageAverageRateLimitExceededPercentage = activeAccountViewModel?.PrimaryUsageAverageRateLimitExceededPercentage ?? 0;
        ActiveAccountSecondaryUsageAverageRateLimitExceededPercentage = activeAccountViewModel?.SecondaryUsageAverageRateLimitExceededPercentage ?? 0;
        ActiveAccountPrimaryUsageAverageRateLimitHeadroomPercentage = activeAccountViewModel?.PrimaryUsageAverageRateLimitHeadroomPercentage ?? 0;
        ActiveAccountSecondaryUsageAverageRateLimitHeadroomPercentage = activeAccountViewModel?.SecondaryUsageAverageRateLimitHeadroomPercentage ?? 0;
    }

    private void SetLowUsageAccounts(IReadOnlyList<ProviderAccountViewModel> accountViewModels)
    {
        var lowUsageAccountViewModels = accountViewModels
            .Where(IsLowUsageAccount)
            .OrderBy(accountViewModel => GetLowestKnownUsageRemainingPercentage(accountViewModel))
            .ThenBy(accountViewModel => accountViewModel.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(accountViewModel => new DashboardLowUsageAccountViewModel(accountViewModel, _localizationService))
            .ToList();

        LowUsageAccounts.Clear();
        foreach (var lowUsageAccountViewModel in lowUsageAccountViewModels) LowUsageAccounts.Add(lowUsageAccountViewModel);

        HasLowUsageAccounts = LowUsageAccounts.Count > 0;
        HasNoLowUsageAccounts = LowUsageAccounts.Count == 0;
    }

    private static bool IsLowUsageAccount(ProviderAccountViewModel accountViewModel) => accountViewModel.IsPrimaryUsageUnderWarningThreshold || accountViewModel.IsSecondaryUsageUnderWarningThreshold;

    private static int GetLowestKnownUsageRemainingPercentage(ProviderAccountViewModel accountViewModel)
    {
        var primaryUsageRemainingPercentage = accountViewModel.IsPrimaryUsageUnderWarningThreshold ? accountViewModel.PrimaryUsageRemainingPercentage : 101;
        var secondaryUsageRemainingPercentage = accountViewModel.IsSecondaryUsageUnderWarningThreshold ? accountViewModel.SecondaryUsageRemainingPercentage : 101;
        return Math.Min(primaryUsageRemainingPercentage, secondaryUsageRemainingPercentage);
    }

    private static int CalculateAverageUsageRemainingPercentage(IEnumerable<ProviderAccountViewModel> accountViewModels, Func<ProviderAccountViewModel, int> remainingPercentageSelector)
    {
        var knownRemainingPercentages = accountViewModels.Select(remainingPercentageSelector).Where(remainingPercentage => remainingPercentage >= 0).ToList();
        if (knownRemainingPercentages.Count == 0) return -1;
        return (int)Math.Round(knownRemainingPercentages.Average(), MidpointRounding.AwayFromZero);
    }

    private static int ClampUsageRemainingPercentage(int usageRemainingPercentage) => usageRemainingPercentage < 0 ? 0 : Math.Clamp(usageRemainingPercentage, 0, 100);

    private string GetProviderDisplayName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => _localizationService.GetLocalizedString("Provider_ClaudeCodeDisplayName"), CliProviderKind.Zai => _localizationService.GetLocalizedString("Provider_ZaiDisplayName"), CliProviderKind.OpenCodeGo => _localizationService.GetLocalizedString("Provider_OpenCodeGoDisplayName"), CliProviderKind.Ollama => _localizationService.GetLocalizedString("Provider_OllamaDisplayName"), _ => _localizationService.GetLocalizedString("Provider_CodexDisplayName") };

    private static DateTimeOffset? GetUsageResetAt(ProviderUsageWindow providerUsageWindow)
    {
        if (providerUsageWindow.ResetAt is not null) return providerUsageWindow.ResetAt;
        if (providerUsageWindow.ResetAfterSeconds < 0) return null;
        return DateTimeOffset.UtcNow.AddSeconds(providerUsageWindow.ResetAfterSeconds);
    }

    private string FormatUsageRemainingPercentage(int usageRemainingPercentage) => usageRemainingPercentage < 0 ? _localizationService.GetLocalizedString("ProviderAccountViewModel_UnknownUsage") : _localizationService.GetFormattedString("ProviderAccountViewModel_UsageRemainingOnlyFormat", usageRemainingPercentage);

    private string FormatLowUsageAccountCount(int lowUsageAccountCount) => lowUsageAccountCount == 0 ? _localizationService.GetLocalizedString("DashboardPageViewModel_NoLowUsageAccounts") : _localizationService.GetFormattedString("DashboardPageViewModel_LowUsageAccountCountFormat", lowUsageAccountCount);


}

public sealed class DashboardLowUsageAccountViewModel(ProviderAccountViewModel accountViewModel, LocalizationService localizationService)
{
    public string DisplayName { get; } = accountViewModel.DisplayName;

    public string EmailAddress { get; } = accountViewModel.EmailAddress;

    public string StatusText { get; } = accountViewModel.StatusText;

    public string LastUsageRefreshText { get; } = accountViewModel.LastUsageRefreshText;

    public bool IsPrimaryUsageWarningVisible { get; } = accountViewModel.IsPrimaryUsageUnderWarningThreshold;

    public bool IsSecondaryUsageWarningVisible { get; } = accountViewModel.IsSecondaryUsageUnderWarningThreshold;

    public string PrimaryUsageWarningText { get; } = localizationService.GetFormattedString("DashboardLowUsageAccountViewModel_LowUsageFormat", accountViewModel.PrimaryUsageWindowLabelText, accountViewModel.PrimaryUsageRemainingText);

    public string SecondaryUsageWarningText { get; } = localizationService.GetFormattedString("DashboardLowUsageAccountViewModel_LowUsageFormat", accountViewModel.SecondaryUsageWindowLabelText, accountViewModel.SecondaryUsageRemainingText);

    public int PrimaryUsageRemainingPercentage { get; } = accountViewModel.PrimaryUsageRemainingPercentage;

    public int SecondaryUsageRemainingPercentage { get; } = accountViewModel.SecondaryUsageRemainingPercentage;
}
