using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Managers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class DashboardPage : Page
{
    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly AccountServiceManager _accountServiceManager = App.Services.GetRequiredService<AccountServiceManager>();
    private readonly ApplicationSettings _applicationSettings = App.Services.GetRequiredService<ApplicationSettings>();

    public DashboardPageViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardPageViewModel>();
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        await RunWithLoadingAsync(_localizationService.GetLocalizedString("AccountsPage_RefreshAllAccountsLoadingMessage"), async () => await _accountServiceManager.RefreshAllAccountsAsync(_applicationSettings.SelectedProviderKind));
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
        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogMessage"), _localizationService.GetLocalizedString("AccountsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var deletedAccountCount = await _accountServiceManager.DeleteExpiredAccountsAsync(_applicationSettings.SelectedProviderKind);
        await ViewModel.ReloadDashboardAsync();
        await this.ShowDialogAsync(_localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsDialogTitle"), deletedAccountCount == 0 ? _localizationService.GetLocalizedString("AccountsPage_DeleteExpiredAccountsNoAccountsMessage") : _localizationService.GetFormattedString("AccountsPage_DeleteExpiredAccountsDeletedMessageFormat", deletedAccountCount));
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
