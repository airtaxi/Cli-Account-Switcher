using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ApplicationNotificationService
{
    public const string NotificationActionArgumentName = "action";

    public const string AccountsNavigationNotificationAction = "openAccounts";

    public void ShowExpiredAccountDetectedNotification(string accountDisplayName)
    {
        var notificationTitle = GetLocalizedString("Notification_ExpiredAccountDetectedTitle");
        var notificationMessage = App.LocalizationService.GetFormattedString("Notification_ExpiredAccountDetectedMessageFormat", accountDisplayName);
        ShowNotification(notificationTitle, notificationMessage);
    }

    public void ShowStoreUpdateAvailableNotification(int availableUpdateCount, Uri storeProductPageAddress)
    {
        var notificationTitle = GetLocalizedString("Notification_StoreUpdateAvailableTitle");
        var notificationMessage = App.LocalizationService.GetFormattedString("Notification_StoreUpdateAvailableMessageFormat", availableUpdateCount);
        var notificationButton = new AppNotificationButton(GetLocalizedString("Notification_OpenStoreButtonText")).SetInvokeUri(storeProductPageAddress);
        ShowNotification(notificationTitle, notificationMessage, notificationButton);
    }

    public void ShowPrimaryUsageLowQuotaNotification(string accountDisplayName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationTitle = GetLocalizedString("Notification_UsageLowQuotaDetectedTitle");
        var usageWindowName = GetLocalizedString("Notification_PrimaryUsageLowQuotaWindowName");
        var notificationMessage = GetUsageLowQuotaNotificationMessage(accountDisplayName, usageWindowName, usageRemainingPercentage, hasAlternativeAccountOverWarningThreshold);
        ShowNotification(notificationTitle, notificationMessage, notificationAction: AccountsNavigationNotificationAction);
    }

    public void ShowSecondaryUsageLowQuotaNotification(string accountDisplayName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationTitle = GetLocalizedString("Notification_UsageLowQuotaDetectedTitle");
        var usageWindowName = GetLocalizedString("Notification_SecondaryUsageLowQuotaWindowName");
        var notificationMessage = GetUsageLowQuotaNotificationMessage(accountDisplayName, usageWindowName, usageRemainingPercentage, hasAlternativeAccountOverWarningThreshold);
        ShowNotification(notificationTitle, notificationMessage, notificationAction: AccountsNavigationNotificationAction);
    }

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

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetUsageLowQuotaNotificationMessage(string accountDisplayName, string usageWindowName, int usageRemainingPercentage, bool hasAlternativeAccountOverWarningThreshold)
    {
        var notificationMessageResourceName = hasAlternativeAccountOverWarningThreshold ? "Notification_UsageLowQuotaDetectedWithSwitchSuggestionMessageFormat" : "Notification_UsageLowQuotaDetectedMessageFormat";
        return App.LocalizationService.GetFormattedString(notificationMessageResourceName, accountDisplayName, usageWindowName, usageRemainingPercentage);
    }
}
