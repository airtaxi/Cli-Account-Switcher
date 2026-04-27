using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace CodexAccountSwitch.WinUI.Services;

public sealed class ApplicationNotificationService
{
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

    private static void ShowNotification(string notificationTitle, string notificationMessage, AppNotificationButton notificationButton = null)
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) return;

            var appNotificationBuilder = new AppNotificationBuilder()
                .AddText(notificationTitle)
                .AddText(notificationMessage);

            if (notificationButton is not null) appNotificationBuilder.AddButton(notificationButton);

            AppNotificationManager.Default.Show(appNotificationBuilder.BuildNotification());
        }
        catch { }
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);
}
