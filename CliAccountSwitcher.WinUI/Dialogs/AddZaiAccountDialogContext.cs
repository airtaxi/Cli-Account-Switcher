using CliAccountSwitcher.WinUI.Services;
using Microsoft.UI.Xaml.Controls;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed class AddZaiAccountDialogContext(ZaiAccountService zaiAccountService, ContentDialog contentDialog)
{
    public ZaiAccountService ZaiAccountService { get; } = zaiAccountService;

    public ContentDialog ContentDialog { get; } = contentDialog;

    public void SetInteractionEnabled(bool isInteractionEnabled) => ContentDialog.IsEnabled = isInteractionEnabled;

    public void CompleteSuccessfully() => ContentDialog.Hide();
}
