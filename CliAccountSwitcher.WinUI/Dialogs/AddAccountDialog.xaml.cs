using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Pages.AddAccountDialog;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
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
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        NavigateToSelectedPage();
    }

    private void NavigateToSelectedPage(bool shouldForceReload = false)
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

        if (!shouldForceReload && AddAccountContentFrame.CurrentSourcePageType == selectedPageType) return;
        AddAccountContentFrame.Navigate(selectedPageType, _addAccountDialogContext);
    }

    private void OnAddAccountModeSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => NavigateToSelectedPage();

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (DispatcherQueue.HasThreadAccess) NavigateToSelectedPage(true);
        else DispatcherQueue.TryEnqueue(() => NavigateToSelectedPage(true));
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => App.ApplicationThemeService.ApplyThemeToElement(this);

    private async void OnAddAccountDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs contentDialogClosingEventArguments)
    {
        App.ApplicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
        var contentDialogClosingDeferral = contentDialogClosingEventArguments.GetDeferral();
        try { await _addAccountDialogContext.DisposeOAuthSessionAsync(); }
        finally { contentDialogClosingDeferral.Complete(); }
    }
}
