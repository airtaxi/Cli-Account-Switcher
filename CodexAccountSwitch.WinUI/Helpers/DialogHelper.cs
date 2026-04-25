using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.WebUI;
using WinRT.Interop;

namespace CodexAccountSwitch.WinUI.Helpers;

public static class DialogHelper
{
    public static async Task<string> ShowInputDialogAsync(this UIElement element, string title = "입력", string placeholderText = "", bool showCancel = false, bool numberOnly = false, string defaultText = "")
    {
        HideOpenContentDialogs(element);

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = "확인",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = element.XamlRoot
        };
        var textBox = new TextBox();
        if (numberOnly) textBox.BeforeTextChanging += (_, e) => e.Cancel = e.NewText.Any(c => !char.IsDigit(c));
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBox.PlaceholderText = placeholderText;
        if (!string.IsNullOrEmpty(defaultText)) textBox.Text = defaultText;
        dialog.Content = textBox;
        if (showCancel) dialog.SecondaryButtonText = "취소";
        TaskCompletionSource<string> taskCompletionSource = new();
        dialog.Closing += (_, args) =>
        {
            if (args.Result == ContentDialogResult.Primary)
                taskCompletionSource.SetResult(textBox.Text.Trim());
            else
                taskCompletionSource.SetResult(null);
        };
        await dialog.ShowAsync();
        return await taskCompletionSource.Task;
    }

    public static ContentDialog GenerateMessageDialog(this UIElement element, string title, string description, string primaryButtonText = "확인", string secondaryButtonText = null)
    {
        HideOpenContentDialogs(element);

        var xamlRoot = element.XamlRoot;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = description,
            PrimaryButtonText = primaryButtonText,
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        if (!string.IsNullOrEmpty(secondaryButtonText)) dialog.SecondaryButtonText = secondaryButtonText;
        return dialog;
    }

    public static async Task<ContentDialogResult> ShowDialogAsync(this UIElement element, string title, string description, string primaryButtonText = "확인", string secondaryButtonText = null)
    {
        var dialog = GenerateMessageDialog(element, title, description, primaryButtonText, secondaryButtonText);
        return await dialog.ShowAsync();
    }

    private static void HideOpenContentDialogs(UIElement element)
    {
        var contentDialogs = VisualTreeHelper.GetOpenPopupsForXamlRoot(element.XamlRoot).Where(x => x.Child is ContentDialog).Select(x => x.Child as ContentDialog);
        if (!contentDialogs.Any()) return;

        foreach (var contentDialog in contentDialogs)
            contentDialog.Hide();
    }
}
