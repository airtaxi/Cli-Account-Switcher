using CodexAccountSwitch.WinUI.Pages.AddAccountDialog;
using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml.Controls;

namespace CodexAccountSwitch.WinUI.Dialogs;

public sealed partial class AddAccountDialog : ContentDialog
{
    private readonly AddAccountDialogContext _addAccountDialogContext;

    public AddAccountDialog(CodexAccountService codexAccountService)
    {
        InitializeComponent();
        _addAccountDialogContext = new AddAccountDialogContext(codexAccountService, this);
        NavigateToSelectedPage();
    }

    private void NavigateToSelectedPage()
    {
        var selectedModeTag = AddAccountModeSelectorBar.SelectedItem?.Tag as string ?? "OAuth";
        var selectedPageType = selectedModeTag switch
        {
            "OAuth" => typeof(OAuthAddAccountPage),
            "ManualPaste" => typeof(ManualPasteAddAccountPage),
            "CurrentAccount" => typeof(CurrentAccountAddAccountPage),
            "File" => typeof(FileAddAccountPage),
            _ => typeof(OAuthAddAccountPage)
        };

        if (AddAccountContentFrame.CurrentSourcePageType == selectedPageType) return;
        AddAccountContentFrame.Navigate(selectedPageType, _addAccountDialogContext);
    }

    private void OnAddAccountModeSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => NavigateToSelectedPage();

    private async void OnAddAccountDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs contentDialogClosingEventArguments)
    {
        var contentDialogClosingDeferral = contentDialogClosingEventArguments.GetDeferral();
        try { await _addAccountDialogContext.DisposeOAuthSessionAsync(); }
        finally { contentDialogClosingDeferral.Complete(); }
    }
}
