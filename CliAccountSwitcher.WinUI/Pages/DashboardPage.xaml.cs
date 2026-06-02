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
        await RunWithLoadingAsync(App.LocalizationService.GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await App.AccountServiceManager.RefreshAllAccountsAsync(App.ApplicationSettings.SelectedProviderKind));
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
        var contentDialogResult = await this.ShowDialogAsync(App.LocalizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), App.LocalizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), App.LocalizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), App.LocalizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedAccountCount = await App.AccountServiceManager.DeleteExpiredAccountsAsync(App.ApplicationSettings.SelectedProviderKind);
        await ViewModel.ReloadDashboardAsync();
        await this.ShowDialogAsync(App.LocalizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedAccountCount == 0 ? App.LocalizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : App.LocalizationService.GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedAccountCount));
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
        finally { MainWindow.HideLoading(); }
    }


}
