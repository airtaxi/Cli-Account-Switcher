using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
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
    private readonly ApplicationSettings _applicationSettings = App.Services.GetRequiredService<ApplicationSettings>();

    public DashboardPageViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardPageViewModel>();
        InitializeComponent();
    }

    private async void OnRefreshAllAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await RunWithLoadingAsync(ViewModel.RefreshAllAccountsLoadingMessage, ViewModel.RefreshAllAccountsAsync);

    private async void OnAddAccountButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = _applicationSettings.SelectedProviderKind;
        if (selectedProviderKind == CliProviderKind.Zai)
        {
            var addZaiAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddZaiAccountDialog
            {
                XamlRoot = XamlRoot
            };
            await addZaiAccountDialog.ShowAsync();
        }
        else if (selectedProviderKind == CliProviderKind.OpenCodeGo)
        {
            var addOpenCodeGoAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddOpenCodeGoAccountDialog
            {
                XamlRoot = XamlRoot
            };
            await addOpenCodeGoAccountDialog.ShowAsync();
        }
        else
        {
            var addAccountDialog = new CliAccountSwitcher.WinUI.Dialogs.AddAccountDialog
            {
                XamlRoot = XamlRoot
            };
            await addAccountDialog.ShowAsync();
        }

        await ViewModel.ReloadDashboardAsync();
    }

    private async void OnDeleteExpiredAccountsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var contentDialogResult = await ShowDialogAsync(ViewModel.CreateDeleteExpiredAccountsConfirmationDialogData());
        if (contentDialogResult != ContentDialogResult.Primary) return;

        var dialogData = await ViewModel.DeleteExpiredAccountsAsync();
        await ShowDialogAsync(dialogData);
    }

    private void OnDashboardPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task RunWithLoadingAsync(string loadingMessage, Func<Task> action)
    {
        MainWindow.ShowLoading(loadingMessage);
        try { await action(); }
        finally { MainWindow.HideLoading(); }
    }

    private async Task<ContentDialogResult> ShowDialogAsync(BasicDialogData dialogData) => await this.ShowDialogAsync(dialogData.Title, dialogData.Message, dialogData.PrimaryButtonText, dialogData.SecondaryButtonText);


}
