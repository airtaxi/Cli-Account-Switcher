using CliAccountSwitcher.WinUI.Pages.AddZaiAccountDialog;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed partial class AddZaiAccountDialog : ContentDialog
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();
    private readonly ZaiAccountService _zaiAccountService = App.Services.GetRequiredService<ZaiAccountService>();
    private readonly AddZaiAccountDialogContext _addZaiAccountDialogContext;

    public AddZaiAccountDialog()
    {
        InitializeComponent();
        _applicationThemeService.ApplyThemeToElement(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
        _addZaiAccountDialogContext = new AddZaiAccountDialogContext(_zaiAccountService, this);
        NavigateToSelectedPage();
    }

    private void NavigateToSelectedPage(bool shouldForceReload = false)
    {
        var selectedModeTag = AddAccountModeSelectorBar.SelectedItem?.Tag as string ?? "CurrentAccount";
        var selectedPageType = selectedModeTag switch
        {
            "ApiKey" => typeof(ApiKeyAddAccountPage),
            _ => typeof(CurrentAccountAddAccountPage)
        };

        if (!shouldForceReload && AddAccountContentFrame.CurrentSourcePageType == selectedPageType) return;
        AddAccountContentFrame.Navigate(selectedPageType, _addZaiAccountDialogContext);
    }

    private void OnAddAccountModeSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArguments) => NavigateToSelectedPage();

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToElement(this);

    private void OnAddZaiAccountDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs contentDialogClosingEventArguments) => _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
}
