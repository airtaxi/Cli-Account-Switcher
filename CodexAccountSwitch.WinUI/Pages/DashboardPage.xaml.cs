using CodexAccountSwitch.WinUI.Helpers;
using CodexAccountSwitch.WinUI.Models;
using CodexAccountSwitch.WinUI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPageViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = new DashboardPageViewModel(App.CodexAccountService, App.ApplicationSettings, DispatcherQueue);
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await App.CodexAccountService.RefreshAllAccountsAsync());

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CodexAccountSwitch.WinUI.Dialogs.AddAccountDialog(App.CodexAccountService)
        {
            XamlRoot = XamlRoot
        };
        await addAccountDialog.ShowAsync();
        ViewModel.ReloadDashboard();
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedAccountCount = await App.CodexAccountService.DeleteExpiredAccountsAsync();
        ViewModel.ReloadDashboard();
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedAccountCount == 0 ? GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedAccountCount));
    }

    private void OnManageAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => WeakReferenceMessenger.Default.Send(new ValueChangedMessage<MainPageNavigationSection>(MainPageNavigationSection.Accounts));

    private void OnDashboardPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action)
    {
        MainWindow.ShowLoading(loadingMessage);
        try
        {
            await action();
            ViewModel.ReloadDashboard();
        }
        finally
        {
            MainWindow.HideLoading();
        }
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
