using CodexAccountSwitch.WinUI.Models;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<MainPageNavigationSection>>(this, OnMainPageNavigationSectionChangedMessageReceived);
        NavigateToSection(MainPageNavigationSection.Dashboard);
    }

    private void NavigateToSection(MainPageNavigationSection mainPageNavigationSection)
    {
        var selectedPageType = GetSelectedPageType(mainPageNavigationSection);

        if (SectionContentFrame.CurrentSourcePageType == selectedPageType) return;
        SectionContentFrame.Navigate(selectedPageType);
    }

    private static Type GetSelectedPageType(MainPageNavigationSection mainPageNavigationSection) => mainPageNavigationSection switch
    {
        MainPageNavigationSection.Dashboard => typeof(DashboardPage),
        MainPageNavigationSection.Accounts => typeof(AccountsPage),
        MainPageNavigationSection.About => typeof(AboutPage),
        MainPageNavigationSection.Settings => typeof(SettingsPage),
        _ => throw new ArgumentOutOfRangeException(nameof(mainPageNavigationSection), mainPageNavigationSection, "Unknown main page navigation section.")
    };

    private void OnMainPageNavigationSectionChangedMessageReceived(object messageRecipient, ValueChangedMessage<MainPageNavigationSection> valueChangedMessage) => NavigateToSection(valueChangedMessage.Value);

    private void OnMainPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<MainPageNavigationSection>>(this);
}
