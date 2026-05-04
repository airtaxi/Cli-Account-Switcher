using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPageViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = new DashboardPageViewModel(App.AccountServiceManager, App.ApplicationSettings, DispatcherQueue);
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        await RunWithLoadingAsync(GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await App.AccountServiceManager.RefreshAllAccountsAsync(App.ApplicationSettings.SelectedProviderKind));
    }

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var addAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddAccountDialog
        {
            XamlRoot = XamlRoot
        };
        await addAccountDialog.ShowAsync();
        await ViewModel.ReloadDashboardAsync();
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), GetLocalizedString("AccountsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedAccountCount = await App.AccountServiceManager.DeleteExpiredAccountsAsync(App.ApplicationSettings.SelectedProviderKind);
        await ViewModel.ReloadDashboardAsync();
        await this.ShowDialogAsync(GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedAccountCount == 0 ? GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedAccountCount));
    }

    private void OnDashboardPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action, bool shouldReloadDashboard = true)
    {
        MainWindow.ShowLoading(loadingMessage);
        try
        {
            await action();
            if (shouldReloadDashboard) await ViewModel.ReloadDashboardAsync();
        }
        finally
        {
            MainWindow.HideLoading();
        }
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
