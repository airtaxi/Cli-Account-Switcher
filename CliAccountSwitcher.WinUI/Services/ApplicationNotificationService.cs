using CliAccountSwitcher.Api.Providers.Abstractions;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ApplicationNotificationService
{
    public const string NotificationActionArgumentName = "action";

    public const string AccountsNavigationNotificationAction = "openAccounts";

    private readonly LocalizationService _localizationService;

    public ApplicationNotificationService(LocalizationService localizationService) => _localizationService = localizationService;

    public void ShowExpiredAccountDetectedNotification(string accountDisplayName)
    {
        var notificationTitle = _localizationService.GetLocalizedString("Notification_ExpiredAccountDetectedTitle");
        var notificationMessage = _localizationService.GetFormattedString("Notification_ExpiredAccountDetectedMessageFormat", accountDisplayName);
        ShowNotification(notificationTitle, notificationMessage);
    }

    public void ShowStoreUpdateAvailableNotification(int availableUpdateCount, Uri storeProductPageAddress)
    {
        var notificationTitle = _localizationService.GetLocalizedString("Notification_StoreUpdateAvailableTitle");
        var notificationMessage = _localizationService.GetFormattedString("Notification_StoreUpdateAvailableMessageFormat", availableUpdateCount);
        var notificationButton = new AppNotificationButton(_localizationService.GetLocalizedString("Notification_OpenStoreButtonText")).SetInvokeUri(storeProductPageAddress);
        ShowNotification(notificationTitle, notificationMessage, notificationButton);
    }

    public void ShowPrimaryUsageLowQuotaNotification(string accountDisplayName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationTitle = _localizationService.GetLocalizedString("Notification_UsageLowQuotaDetectedTitle");
        var usageWindowName = _localizationService.GetLocalizedString("Notification_PrimaryUsageLowQuotaWindowName");
        var notificationMessage = GetUsageLowQuotaNotificationMessage(accountDisplayName, usageWindowName, usageRemainingPercentage, hasAlternativeAccountOverWarningThreshold);
        ShowNotification(notificationTitle, notificationMessage, notificationAction: AccountsNavigationNotificationAction);
    }

    public void ShowSecondaryUsageLowQuotaNotification(string accountDisplayName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationTitle = _localizationService.GetLocalizedString("Notification_UsageLowQuotaDetectedTitle");
        var usageWindowName = _localizationService.GetLocalizedString("Notification_SecondaryUsageLowQuotaWindowName");
        var notificationMessage = GetUsageLowQuotaNotificationMessage(accountDisplayName, usageWindowName, usageRemainingPercentage, hasAlternativeAccountOverWarningThreshold);
        ShowNotification(notificationTitle, notificationMessage, notificationAction: AccountsNavigationNotificationAction);
    }

    public void ShowPrimaryUsageSurgeNotification(CliProviderKind providerKind, int usageSurgePercentage, int usageSurgeWindowMinutes)
    {
        var providerDisplayName = GetProviderDisplayName(providerKind);
        var notificationTitle = _localizationService.GetFormattedString("Notification_PrimaryUsageSurgeDetectedTitleFormat", providerDisplayName);
        var notificationMessage = _localizationService.GetFormattedString("Notification_PrimaryUsageSurgeDetectedMessageFormat", usageSurgePercentage, usageSurgeWindowMinutes);
        ShowNotification(notificationTitle, notificationMessage, notificationAction: AccountsNavigationNotificationAction);
    }

    private string GetUsageLowQuotaNotificationMessage(string accountDisplayName, string usageWindowName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationMessageResourceName = hasAlternativeAccountOverWarningThreshold ? "Notification_UsageLowQuotaDetectedWithSwitchSuggestionMessageFormat" : "Notification_UsageLowQuotaDetectedMessageFormat";
        return _localizationService.GetFormattedString(notificationMessageResourceName, accountDisplayName, usageWindowName, usageRemainingPercentage);
    }

    private string GetProviderDisplayName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => _localizationService.GetLocalizedString("Provider_ClaudeCodeDisplayName"), _ => _localizationService.GetLocalizedString("Provider_CodexDisplayName") };

    private static void ShowNotification(string notificationTitle, string notificationMessage, AppNotificationButton notificationButton = null, string notificationAction = "")
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) return;

            var appNotificationBuilder = new AppNotificationBuilder()
                .AddText(notificationTitle)
                .AddText(notificationMessage);

            if (notificationButton is not null) appNotificationBuilder.AddButton(notificationButton);
            if (!string.IsNullOrWhiteSpace(notificationAction)) appNotificationBuilder.AddArgument(NotificationActionArgumentName, notificationAction);

            AppNotificationManager.Default.Show(appNotificationBuilder.BuildNotification());
        }
        catch { }
    }
}
