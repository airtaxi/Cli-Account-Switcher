using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class TaskbarUsageControlViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan s_primaryUsageWindowDuration = TimeSpan.FromHours(5);
    private static readonly TimeSpan s_secondaryUsageWindowDuration = TimeSpan.FromDays(7);

    private readonly AccountServiceManager _accountServiceManager;
    private readonly ApplicationSettings _applicationSettings;
    private readonly LocalizationService _localizationService;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _hasRequestedInitialUsageRefresh;
    private bool _disposed;

    public TaskbarUsageControlViewModel(AccountServiceManager accountServiceManager, ApplicationSettings applicationSettings, LocalizationService localizationService, DispatcherQueue dispatcherQueue)
    {
        _accountServiceManager = accountServiceManager;
        _applicationSettings = applicationSettings;
        _localizationService = localizationService;
        _dispatcherQueue = dispatcherQueue;

        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<ProviderAccountsChangedMessage>>(this, OnProviderAccountsChangedMessageReceived);
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadUsage();
    }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool HasPrimaryUsagePercentage { get; set; }

    [ObservableProperty]
    public partial bool HasSecondaryUsagePercentage { get; set; }

    [ObservableProperty]
    public partial int PrimaryUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial int PrimaryUsagePacemakerPercentage { get; set; }

    [ObservableProperty]
    public partial int PrimaryUsagePacemakerDifferencePercentage { get; set; }

    [ObservableProperty]
    public partial int SecondaryUsageRemainingPercentage { get; set; }

    [ObservableProperty]
    public partial int SecondaryUsagePacemakerPercentage { get; set; }

    [ObservableProperty]
    public partial int SecondaryUsagePacemakerDifferencePercentage { get; set; }

    public string PrimaryUsageRemainingPercentageText => FormatUsagePercentage(HasPrimaryUsagePercentage, PrimaryUsageRemainingPercentage);

    public string PrimaryUsagePacemakerPercentageText => FormatUsageDifferencePercentage(HasPrimaryUsagePercentage, PrimaryUsagePacemakerDifferencePercentage);

    public string SecondaryUsageRemainingPercentageText => FormatUsagePercentage(HasSecondaryUsagePercentage, SecondaryUsageRemainingPercentage);

    public string SecondaryUsagePacemakerPercentageText => FormatUsageDifferencePercentage(HasSecondaryUsagePercentage, SecondaryUsagePacemakerDifferencePercentage);

    public bool IsPrimaryUsageBelowPacemaker => PrimaryUsagePacemakerDifferencePercentage < 0;

    public bool IsSecondaryUsageBelowPacemaker => SecondaryUsagePacemakerDifferencePercentage < 0;

    public int PrimaryUsageRemainingProgressBarZIndex => GetRemainingProgressBarZIndex(PrimaryUsageRemainingPercentage, PrimaryUsagePacemakerPercentage, PrimaryUsagePacemakerDifferencePercentage);

    public int PrimaryUsagePacemakerProgressBarZIndex => GetPacemakerProgressBarZIndex(PrimaryUsageRemainingPercentage, PrimaryUsagePacemakerPercentage, PrimaryUsagePacemakerDifferencePercentage);

    public int SecondaryUsageRemainingProgressBarZIndex => GetRemainingProgressBarZIndex(SecondaryUsageRemainingPercentage, SecondaryUsagePacemakerPercentage, SecondaryUsagePacemakerDifferencePercentage);

    public int SecondaryUsagePacemakerProgressBarZIndex => GetPacemakerProgressBarZIndex(SecondaryUsageRemainingPercentage, SecondaryUsagePacemakerPercentage, SecondaryUsagePacemakerDifferencePercentage);

    public string RefreshButtonToolTipText => _localizationService.GetLocalizedString("TaskbarUsageControl_RefreshButtonToolTip");

    public void ReloadUsage()
    {
        var activeAccount = GetActiveAccount();
        SetUsage(activeAccount);
    }

    public async Task ReloadUsageOrRefreshMissingActiveUsageAsync()
    {
        ReloadUsage();
        if (_hasRequestedInitialUsageRefresh || IsRefreshing || HasPrimaryUsagePercentage && HasSecondaryUsagePercentage) return;
        if (GetActiveAccount() is null) return;

        _hasRequestedInitialUsageRefresh = true;
        await RefreshActiveAccountAsync();
    }

    private ProviderAccount GetActiveAccount() => _accountServiceManager.GetAccounts(_applicationSettings.SelectedProviderKind).FirstOrDefault(providerAccount => providerAccount.IsActive);

    private void SetUsage(ProviderAccount activeAccount)
    {
        SetPrimaryUsage(activeAccount?.LastProviderUsageSnapshot?.FiveHour);
        SetSecondaryUsage(activeAccount?.LastProviderUsageSnapshot?.SevenDay);
        RefreshComputedProperties();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<ProviderAccountsChangedMessage>>(this);
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    [RelayCommand]
    private async Task RefreshActiveAccountAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;
        try { await _accountServiceManager.RefreshActiveAccountAsync(_applicationSettings.SelectedProviderKind); }
        catch { }
        finally
        {
            ReloadUsage();
            IsRefreshing = false;
        }
    }

    private void SetPrimaryUsage(ProviderUsageWindow providerUsageWindow)
    {
        var hasUsagePercentage = TryGetUsagePercentages(providerUsageWindow, s_primaryUsageWindowDuration, out var remainingPercentage, out var pacemakerPercentage, out var pacemakerDifferencePercentage);
        HasPrimaryUsagePercentage = hasUsagePercentage;
        PrimaryUsageRemainingPercentage = remainingPercentage;
        PrimaryUsagePacemakerPercentage = pacemakerPercentage;
        PrimaryUsagePacemakerDifferencePercentage = pacemakerDifferencePercentage;
    }

    private void SetSecondaryUsage(ProviderUsageWindow providerUsageWindow)
    {
        var hasUsagePercentage = TryGetUsagePercentages(providerUsageWindow, s_secondaryUsageWindowDuration, out var remainingPercentage, out var pacemakerPercentage, out var pacemakerDifferencePercentage);
        HasSecondaryUsagePercentage = hasUsagePercentage;
        SecondaryUsageRemainingPercentage = remainingPercentage;
        SecondaryUsagePacemakerPercentage = pacemakerPercentage;
        SecondaryUsagePacemakerDifferencePercentage = pacemakerDifferencePercentage;
    }

    private void OnProviderAccountsChangedMessageReceived(object recipient, ValueChangedMessage<ProviderAccountsChangedMessage> valueChangedMessage)
    {
        if (_applicationSettings.SelectedProviderKind != valueChangedMessage.Value.ProviderKind) return;
        QueueReloadUsage();
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage) => QueueReloadUsage();

    private void OnLocalizationServiceLanguageChanged()
    {
        if (_dispatcherQueue.HasThreadAccess) RefreshLocalizedProperties();
        else _dispatcherQueue.TryEnqueue(RefreshLocalizedProperties);
    }

    private void QueueReloadUsage()
    {
        if (_dispatcherQueue.HasThreadAccess) ReloadUsage();
        else _dispatcherQueue.TryEnqueue(ReloadUsage);
    }

    private void RefreshLocalizedProperties()
    {
        RefreshComputedProperties();
        OnPropertyChanged(nameof(RefreshButtonToolTipText));
    }

    private void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(PrimaryUsageRemainingPercentageText));
        OnPropertyChanged(nameof(PrimaryUsagePacemakerPercentageText));
        OnPropertyChanged(nameof(SecondaryUsageRemainingPercentageText));
        OnPropertyChanged(nameof(SecondaryUsagePacemakerPercentageText));
        OnPropertyChanged(nameof(IsPrimaryUsageBelowPacemaker));
        OnPropertyChanged(nameof(IsSecondaryUsageBelowPacemaker));
        OnPropertyChanged(nameof(PrimaryUsageRemainingProgressBarZIndex));
        OnPropertyChanged(nameof(PrimaryUsagePacemakerProgressBarZIndex));
        OnPropertyChanged(nameof(SecondaryUsageRemainingProgressBarZIndex));
        OnPropertyChanged(nameof(SecondaryUsagePacemakerProgressBarZIndex));
    }

    private string FormatUsagePercentage(bool hasUsagePercentage, int usagePercentage) => hasUsagePercentage ? _localizationService.GetFormattedString("TaskbarUsageControl_PercentageFormat", Math.Clamp(usagePercentage, 0, 100)) : _localizationService.GetLocalizedString("TaskbarUsageControl_UnknownPercentageText");

    private string FormatUsageDifferencePercentage(bool hasUsagePercentage, int usagePercentage) => hasUsagePercentage ? _localizationService.GetFormattedString("TaskbarUsageControl_PercentageDifferenceFormat", Math.Clamp(usagePercentage, -100, 100)) : _localizationService.GetLocalizedString("TaskbarUsageControl_UnknownPercentageText");

    private static int GetRemainingProgressBarZIndex(int remainingPercentage, int pacemakerPercentage, int pacemakerDifferencePercentage) => pacemakerDifferencePercentage < 0 || pacemakerDifferencePercentage == 0 && remainingPercentage <= pacemakerPercentage ? 1 : 0;

    private static int GetPacemakerProgressBarZIndex(int remainingPercentage, int pacemakerPercentage, int pacemakerDifferencePercentage) => pacemakerDifferencePercentage > 0 || pacemakerDifferencePercentage == 0 && pacemakerPercentage < remainingPercentage ? 1 : 0;

    private static bool TryGetUsagePercentages(ProviderUsageWindow providerUsageWindow, TimeSpan usageWindowDuration, out int remainingPercentage, out int pacemakerPercentage, out int pacemakerDifferencePercentage)
    {
        remainingPercentage = 0;
        pacemakerPercentage = 0;
        pacemakerDifferencePercentage = 0;

        if (providerUsageWindow is null || providerUsageWindow.RemainingPercentage < 0 || providerUsageWindow.ResetAfterSeconds < 0) return false;
        if (usageWindowDuration <= TimeSpan.Zero) return false;

        remainingPercentage = Math.Clamp(providerUsageWindow.RemainingPercentage, 0, 100);
        var pacemakerRemainingPercentage = Math.Clamp((TimeSpan.FromSeconds(providerUsageWindow.ResetAfterSeconds).TotalSeconds / usageWindowDuration.TotalSeconds) * 100.0, 0.0, 100.0);
        pacemakerPercentage = Convert.ToInt32(Math.Round(pacemakerRemainingPercentage, MidpointRounding.AwayFromZero));
        pacemakerDifferencePercentage = CalculateUsagePacemakerDifferencePercentage(providerUsageWindow, remainingPercentage, usageWindowDuration);
        return true;
    }

    private static int CalculateUsagePacemakerDifferencePercentage(ProviderUsageWindow providerUsageWindow, int remainingPercentage, TimeSpan usageWindowDuration)
    {
        var usedPercentage = providerUsageWindow.UsedPercentage is >= 0 and <= 100 ? providerUsageWindow.UsedPercentage : 100 - remainingPercentage;
        var elapsedDuration = usageWindowDuration - TimeSpan.FromSeconds(providerUsageWindow.ResetAfterSeconds);
        if (elapsedDuration < TimeSpan.Zero) elapsedDuration = TimeSpan.Zero;

        var averagePaceUsedPercentage = Math.Clamp((elapsedDuration.TotalSeconds / usageWindowDuration.TotalSeconds) * 100.0, 0.0, 100.0);
        var usageAverageRateDifferencePercentage = usedPercentage - averagePaceUsedPercentage;
        if (usageAverageRateDifferencePercentage > 0) return -Convert.ToInt32(Math.Ceiling(usageAverageRateDifferencePercentage));
        if (usageAverageRateDifferencePercentage < 0) return Convert.ToInt32(Math.Ceiling(-usageAverageRateDifferencePercentage));
        return 0;
    }
}
