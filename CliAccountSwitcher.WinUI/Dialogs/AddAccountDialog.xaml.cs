using CliAccountSwitcher.WinUI.Pages.AddAccountDialog;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed partial class AddAccountDialog : ContentDialog
{
    private readonly AddAccountDialogContext _addAccountDialogContext;

    public AddAccountDialog(CodexAccountService codexAccountService)
    {
        InitializeComponent();
        App.ApplicationThemeService.ApplyThemeToElement(this);
        App.ApplicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
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

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToElement(this);

    private async void OnAddAccountDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs contentDialogClosingEventArguments)
    {
        App.ApplicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        var contentDialogClosingDeferral = contentDialogClosingEventArguments.GetDeferral();
        try { await _addAccountDialogContext.DisposeOAuthSessionAsync(); }
        finally { contentDialogClosingDeferral.Complete(); }
    }
}
