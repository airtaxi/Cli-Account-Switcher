using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();

        App.LocalizationService.LanguageChanged += RefreshLocalizedText;
    }

    private void RefreshLocalizedText()
    {
        DashboardSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_DashboardSelectorBarItem/Text");
        AccountsSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_AccountsSelectorBarItem/Text");
        AboutSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_AboutSelectorBarItem/Text");
        SettingsSelectorBarItem.Text = App.LocalizationService.GetLocalizedString("MainPage_SettingsSelectorBarItem/Text");
    }

    public void NavigateToAccountsSection()
    {
        PageSelectorBar.SelectedItem = AccountsSelectorBarItem;
        NavigateToSelectedSection();
    }

    private void NavigateToSelectedSection()
    {
        var selectedSectionTag = PageSelectorBar.SelectedItem?.Tag as string ?? "Dashboard";
        var selectedPageType = GetSelectedPageType(selectedSectionTag);

        if (SectionContentFrame.CurrentSourcePageType == selectedPageType) return;
        SectionContentFrame.Navigate(selectedPageType);
    }

    private Type GetSelectedPageType(string selectedSectionTag) => selectedSectionTag switch
    {
        "Dashboard" => typeof(DashboardPage),
        "Accounts" => typeof(AccountsPage),
        "About" => typeof(AboutPage),
        "Settings" => typeof(SettingsPage),
        _ => throw new InvalidOperationException($"Unknown section tag: {selectedSectionTag}")
    };

    private void OnPageSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArgs) => NavigateToSelectedSection();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {

    }
}
