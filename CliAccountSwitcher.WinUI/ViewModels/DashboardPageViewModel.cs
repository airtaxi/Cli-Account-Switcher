using CliAccountSwitcher.Api.Providers.Abstractions;
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
    private readonly CodexAccountService _codexAccountService;
    private readonly ApplicationSettings _applicationSettings;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _disposed;

    public DashboardPageViewModel(CodexAccountService codexAccountService, ApplicationSettings applicationSettings, DispatcherQueue dispatcherQueue)
    {
        _codexAccountService = codexAccountService;
        _applicationSettings = applicationSettings;
        _dispatcherQueue = dispatcherQueue;
        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CodexAccount>>(this, OnCodexAccountChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CodexAccountStoreDocument>>(this, OnCodexAccountStoreDocumentChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadDashboard();
    }

    public ObservableCollection<DashboardLowUsageAccountViewModel> LowUsageAccounts { get; } = [];

    [ObservableProperty]
    public partial string AccountCountText { get; set; } = "";

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
    public partial string ActiveAccountLastUsageRefreshText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActiveAccountPrimaryUsageUnderWarningThreshold { get; set; }

    [ObservableProperty]
    public partial bool IsActiveAccountSecondaryUsageUnderWarningThreshold { get; set; }

    [ObservableProperty]
    public partial bool HasLowUsageAccounts { get; set; }

    [ObservableProperty]
    public partial bool HasNoLowUsageAccounts { get; set; } = true;

    public void ReloadDashboard()
    {
        if (_applicationSettings.SelectedProviderKind == CliProviderKind.Codex)
        {
            ReloadCodexDashboard();
            return;
        }

        _ = ReloadDashboardAsync();
    }

    public async Task ReloadDashboardAsync()
    {
        if (_applicationSettings.SelectedProviderKind == CliProviderKind.Codex)
        {
            ReloadCodexDashboard();
            return;
        }

        await ReloadClaudeCodeDashboardAsync();
    }

    public async Task RefreshActiveProviderAccountAsync() => await ReloadDashboardAsync();

    private void ReloadCodexDashboard()
    {
        var codexAccounts = _codexAccountService.GetAccounts();
        var accountViewModels = codexAccounts.Select(codexAccount => new CodexAccountViewModel(codexAccount, _applicationSettings)).ToList();

        AccountCountText = GetFormattedString("DashboardPageViewModel_AccountCountFormat", accountViewModels.Count);
        SetAverageUsageProperties(codexAccounts);
        SetLowUsageSummaryProperties(accountViewModels);
        SetActiveAccountProperties(accountViewModels.FirstOrDefault(accountViewModel => accountViewModel.IsActive));
        SetLowUsageAccounts(accountViewModels);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applicationSettings.PropertyChanged -= OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CodexAccount>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CodexAccountStoreDocument>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(ApplicationSettings.PrimaryUsageWarningThresholdPercentage) and not nameof(ApplicationSettings.SecondaryUsageWarningThresholdPercentage)) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void OnCodexAccountChangedMessageReceived(object recipient, ValueChangedMessage<CodexAccount> valueChangedMessage)
    {
        if (_applicationSettings.SelectedProviderKind != CliProviderKind.Codex) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void OnCodexAccountStoreDocumentChangedMessageReceived(object recipient, ValueChangedMessage<CodexAccountStoreDocument> valueChangedMessage)
    {
        if (_applicationSettings.SelectedProviderKind != CliProviderKind.Codex) return;
        if (_dispatcherQueue.HasThreadAccess) ReloadDashboard();
        else _dispatcherQueue.TryEnqueue(ReloadDashboard);
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage) => QueueReloadDashboard();

    private async Task ReloadClaudeCodeDashboardAsync()
    {
        try
        {
            var storedProviderAccounts = await App.CliProviderAccountService.GetClaudeCodeAccountsAsync();
            var accountViewModels = storedProviderAccounts.Select(storedProviderAccount => new CodexAccountViewModel(storedProviderAccount, _applicationSettings)).ToList();
            foreach (var accountViewModel in accountViewModels) ApplyClaudeCodeUsageSnapshotCache(accountViewModel);

            var activeAccountViewModel = accountViewModels.FirstOrDefault(accountViewModel => accountViewModel.IsActive);

            AccountCountText = GetFormattedString("DashboardPageViewModel_AccountCountFormat", accountViewModels.Count);
            SetAverageUsageProperties(accountViewModels);
            SetLowUsageSummaryProperties(accountViewModels);
            SetActiveAccountProperties(activeAccountViewModel);
            SetLowUsageAccounts(accountViewModels);
        }
        catch
        {
            AccountCountText = GetFormattedString("DashboardPageViewModel_AccountCountFormat", 0);
            SetUnknownAverageUsageProperties();
            SetLowUsageSummaryProperties([]);
            SetActiveAccountProperties(null);
            SetLowUsageAccounts([]);
        }
    }

    private void QueueReloadDashboard()
    {
        if (_dispatcherQueue.HasThreadAccess) _ = ReloadDashboardAsync();
        else _dispatcherQueue.TryEnqueue(() => _ = ReloadDashboardAsync());
    }

    private static void ApplyClaudeCodeUsageSnapshotCache(CodexAccountViewModel accountViewModel)
    {
        if (App.CliProviderAccountService.TryGetClaudeCodeUsageSnapshot(accountViewModel.AccountIdentifier, out var providerUsageSnapshot))
        {
            var usageRefreshTime = App.CliProviderAccountService.TryGetClaudeCodeUsageRefreshTime(accountViewModel.AccountIdentifier, out var cachedUsageRefreshTime) ? cachedUsageRefreshTime : DateTimeOffset.UtcNow;
            accountViewModel.UpdateProviderUsageSnapshot(providerUsageSnapshot, usageRefreshTime);
        }
    }

    private void SetAverageUsageProperties(IReadOnlyList<CodexAccount> codexAccounts)
    {
        var primaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(codexAccounts, codexAccount => codexAccount.LastCodexUsageSnapshot.PrimaryWindow.RemainingPercentage);
        var secondaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(codexAccounts, codexAccount => codexAccount.LastCodexUsageSnapshot.SecondaryWindow.RemainingPercentage);

        PrimaryAverageUsageRemainingText = FormatUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        PrimaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingText = FormatUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
    }

    private void SetAverageUsageProperties(IReadOnlyList<CodexAccountViewModel> accountViewModels)
    {
        var primaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(accountViewModels, accountViewModel => accountViewModel.ProviderUsageSnapshot.FiveHour.RemainingPercentage);
        var secondaryAverageUsageRemainingPercentage = CalculateAverageUsageRemainingPercentage(accountViewModels, accountViewModel => accountViewModel.ProviderUsageSnapshot.SevenDay.RemainingPercentage);

        PrimaryAverageUsageRemainingText = FormatUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        PrimaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(primaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingText = FormatUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
        SecondaryAverageUsageRemainingPercentage = ClampUsageRemainingPercentage(secondaryAverageUsageRemainingPercentage);
    }

    private void SetUnknownAverageUsageProperties()
    {
        PrimaryAverageUsageRemainingText = GetLocalizedString("CodexAccountViewModel_UnknownUsage");
        PrimaryAverageUsageRemainingPercentage = 0;
        SecondaryAverageUsageRemainingText = GetLocalizedString("CodexAccountViewModel_UnknownUsage");
        SecondaryAverageUsageRemainingPercentage = 0;
    }

    private void SetLowUsageSummaryProperties(IReadOnlyList<CodexAccountViewModel> accountViewModels)
    {
        var primaryLowUsageAccountCount = accountViewModels.Count(accountViewModel => accountViewModel.IsPrimaryUsageUnderWarningThreshold);
        var secondaryLowUsageAccountCount = accountViewModels.Count(accountViewModel => accountViewModel.IsSecondaryUsageUnderWarningThreshold);

        PrimaryLowUsageAccountCountText = FormatLowUsageAccountCount(primaryLowUsageAccountCount);
        SecondaryLowUsageAccountCountText = FormatLowUsageAccountCount(secondaryLowUsageAccountCount);
    }

    private void SetActiveAccountProperties(CodexAccountViewModel activeAccountViewModel)
    {
        HasActiveAccount = activeAccountViewModel is not null;
        HasNoActiveAccount = activeAccountViewModel is null;

        ActiveAccountDisplayNameText = activeAccountViewModel?.DisplayName ?? "";
        ActiveAccountEmailAddressText = activeAccountViewModel?.EmailAddress ?? "";
        ActiveAccountPlanText = activeAccountViewModel?.PlanText ?? "";
        ActiveAccountPrimaryUsageRemainingText = activeAccountViewModel?.PrimaryUsageRemainingText ?? GetLocalizedString("CodexAccountViewModel_UnknownUsage");
        ActiveAccountSecondaryUsageRemainingText = activeAccountViewModel?.SecondaryUsageRemainingText ?? GetLocalizedString("CodexAccountViewModel_UnknownUsage");
        ActiveAccountPrimaryUsageRemainingPercentage = activeAccountViewModel?.PrimaryUsageRemainingPercentage ?? 0;
        ActiveAccountSecondaryUsageRemainingPercentage = activeAccountViewModel?.SecondaryUsageRemainingPercentage ?? 0;
        ActiveAccountLastUsageRefreshText = activeAccountViewModel?.LastUsageRefreshText ?? "";
        IsActiveAccountPrimaryUsageUnderWarningThreshold = activeAccountViewModel?.IsPrimaryUsageUnderWarningThreshold == true;
        IsActiveAccountSecondaryUsageUnderWarningThreshold = activeAccountViewModel?.IsSecondaryUsageUnderWarningThreshold == true;
    }

    private void SetLowUsageAccounts(IReadOnlyList<CodexAccountViewModel> accountViewModels)
    {
        var lowUsageAccountViewModels = accountViewModels
            .Where(IsLowUsageAccount)
            .OrderBy(accountViewModel => GetLowestKnownUsageRemainingPercentage(accountViewModel))
            .ThenBy(accountViewModel => accountViewModel.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(accountViewModel => new DashboardLowUsageAccountViewModel(accountViewModel))
            .ToList();

        LowUsageAccounts.Clear();
        foreach (var lowUsageAccountViewModel in lowUsageAccountViewModels) LowUsageAccounts.Add(lowUsageAccountViewModel);

        HasLowUsageAccounts = LowUsageAccounts.Count > 0;
        HasNoLowUsageAccounts = LowUsageAccounts.Count == 0;
    }

    private static bool IsLowUsageAccount(CodexAccountViewModel accountViewModel) => accountViewModel.IsPrimaryUsageUnderWarningThreshold || accountViewModel.IsSecondaryUsageUnderWarningThreshold;

    private static int GetLowestKnownUsageRemainingPercentage(CodexAccountViewModel accountViewModel)
    {
        var primaryUsageRemainingPercentage = accountViewModel.IsPrimaryUsageUnderWarningThreshold ? accountViewModel.PrimaryUsageRemainingPercentage : 101;
        var secondaryUsageRemainingPercentage = accountViewModel.IsSecondaryUsageUnderWarningThreshold ? accountViewModel.SecondaryUsageRemainingPercentage : 101;
        return Math.Min(primaryUsageRemainingPercentage, secondaryUsageRemainingPercentage);
    }

    private static int CalculateAverageUsageRemainingPercentage(IEnumerable<CodexAccount> codexAccounts, Func<CodexAccount, int> remainingPercentageSelector)
    {
        var knownRemainingPercentages = codexAccounts.Select(remainingPercentageSelector).Where(remainingPercentage => remainingPercentage >= 0).ToList();
        if (knownRemainingPercentages.Count == 0) return -1;
        return (int)Math.Round(knownRemainingPercentages.Average(), MidpointRounding.AwayFromZero);
    }

    private static int CalculateAverageUsageRemainingPercentage(IEnumerable<CodexAccountViewModel> accountViewModels, Func<CodexAccountViewModel, int> remainingPercentageSelector)
    {
        var knownRemainingPercentages = accountViewModels.Select(remainingPercentageSelector).Where(remainingPercentage => remainingPercentage >= 0).ToList();
        if (knownRemainingPercentages.Count == 0) return -1;
        return (int)Math.Round(knownRemainingPercentages.Average(), MidpointRounding.AwayFromZero);
    }

    private static int ClampUsageRemainingPercentage(int usageRemainingPercentage) => usageRemainingPercentage < 0 ? 0 : Math.Clamp(usageRemainingPercentage, 0, 100);

    private static string FormatUsageRemainingPercentage(int usageRemainingPercentage) => usageRemainingPercentage < 0 ? GetLocalizedString("CodexAccountViewModel_UnknownUsage") : GetFormattedString("CodexAccountViewModel_UsageRemainingOnlyFormat", usageRemainingPercentage);

    private static string FormatLowUsageAccountCount(int lowUsageAccountCount) => lowUsageAccountCount == 0 ? GetLocalizedString("DashboardPageViewModel_NoLowUsageAccounts") : GetFormattedString("DashboardPageViewModel_LowUsageAccountCountFormat", lowUsageAccountCount);

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}

public sealed class DashboardLowUsageAccountViewModel(CodexAccountViewModel accountViewModel)
{
    public string DisplayName { get; } = accountViewModel.DisplayName;

    public string EmailAddress { get; } = accountViewModel.EmailAddress;

    public string StatusText { get; } = accountViewModel.StatusText;

    public string LastUsageRefreshText { get; } = accountViewModel.LastUsageRefreshText;

    public bool IsPrimaryUsageWarningVisible { get; } = accountViewModel.IsPrimaryUsageUnderWarningThreshold;

    public bool IsSecondaryUsageWarningVisible { get; } = accountViewModel.IsSecondaryUsageUnderWarningThreshold;

    public string PrimaryUsageWarningText { get; } = FormatLowUsageText(accountViewModel.PrimaryUsageWindowLabelText, accountViewModel.PrimaryUsageRemainingText);

    public string SecondaryUsageWarningText { get; } = FormatLowUsageText(accountViewModel.SecondaryUsageWindowLabelText, accountViewModel.SecondaryUsageRemainingText);

    public int PrimaryUsageRemainingPercentage { get; } = accountViewModel.PrimaryUsageRemainingPercentage;

    public int SecondaryUsageRemainingPercentage { get; } = accountViewModel.SecondaryUsageRemainingPercentage;

    private static string FormatLowUsageText(string usageWindowLabelText, string usageRemainingText) => App.LocalizationService.GetFormattedString("DashboardLowUsageAccountViewModel_LowUsageFormat", usageWindowLabelText, usageRemainingText);
}
