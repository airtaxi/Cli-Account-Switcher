using CliAccountSwitcher.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Helpers;

public static class DialogHelper
{
    private static readonly Lazy<LocalizationService> s_localizationServiceLazy = new(() => App.Services.GetRequiredService<LocalizationService>());
    private static readonly Lazy<ApplicationThemeService> s_applicationThemeServiceLazy = new(() => App.Services.GetRequiredService<ApplicationThemeService>());
    private static LocalizationService s_localizationService => s_localizationServiceLazy.Value;
    private static ApplicationThemeService s_applicationThemeService => s_applicationThemeServiceLazy.Value;
    public static async Task<string> ShowInputDialogAsync(this UIElement element, string title = null, string placeholderText = "", bool showCancel = false, bool numberOnly = false, string defaultText = "")
    {
        HideOpenContentDialogs(element);

        var dialog = new ContentDialog
        {
            Title = title ?? s_localizationService.GetLocalizedString("DialogHelper_DefaultInputTitle"),
            PrimaryButtonText = s_localizationService.GetLocalizedString("DialogHelper_ConfirmButtonText"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = element.XamlRoot
        };
        s_applicationThemeService.RegisterThemeTarget(dialog);
        var textBox = new TextBox();
        if (numberOnly) textBox.BeforeTextChanging += (textBoxSender, textBoxBeforeTextChangingEventArguments) => textBoxBeforeTextChangingEventArguments.Cancel = textBoxBeforeTextChangingEventArguments.NewText.Any(character => !char.IsDigit(character));
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBox.PlaceholderText = placeholderText;
        if (!string.IsNullOrEmpty(defaultText)) textBox.Text = defaultText;
        dialog.Content = textBox;
        if (showCancel) dialog.SecondaryButtonText = s_localizationService.GetLocalizedString("DialogHelper_CancelButtonText");
        TaskCompletionSource<string> taskCompletionSource = new();
        dialog.Closing += (contentDialogSender, contentDialogClosingEventArguments) =>
        {
            taskCompletionSource.SetResult(contentDialogClosingEventArguments.Result == ContentDialogResult.Primary ? textBox.Text.Trim() : null);
        };
        await dialog.ShowAsync();
        return await taskCompletionSource.Task;
    }

    public static ContentDialog GenerateMessageDialog(this UIElement element, string title, string description, string primaryButtonText = null, string secondaryButtonText = null)
    {
        HideOpenContentDialogs(element);

        var xamlRoot = element.XamlRoot;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = description,
            PrimaryButtonText = primaryButtonText ?? s_localizationService.GetLocalizedString("DialogHelper_ConfirmButtonText"),
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        s_applicationThemeService.RegisterThemeTarget(dialog);

        if (!string.IsNullOrEmpty(secondaryButtonText)) dialog.SecondaryButtonText = secondaryButtonText;
        return dialog;
    }

    public static async Task<ContentDialogResult> ShowDialogAsync(this UIElement element, string title, string description, string primaryButtonText = null, string secondaryButtonText = null)
    {
        var dialog = GenerateMessageDialog(element, title, description, primaryButtonText, secondaryButtonText);
        return await dialog.ShowAsync();
    }

    private static void HideOpenContentDialogs(UIElement element)
    {
        var contentDialogs = VisualTreeHelper.GetOpenPopupsForXamlRoot(element.XamlRoot).Where(popup => popup.Child is ContentDialog).Select(popup => popup.Child as ContentDialog);
        if (!contentDialogs.Any()) return;

        foreach (var contentDialog in contentDialogs) contentDialog.Hide();
    }

}
