using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class SkillsPage : Page
{
    public SkillsPageViewModel ViewModel { get; }

    public SkillsPage()
    {
        ViewModel = new SkillsPageViewModel(App.SkillService, App.ApplicationSettings, DispatcherQueue);
        InitializeComponent();
    }

    private CliProviderKind SelectedProviderKind => ViewModel.SelectedProviderKind;

    private async void OnDeleteSelectedSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedSkillItems = ViewModel.FilteredSkills.Where(skillItem => skillItem.IsSelected).ToArray();
        if (selectedSkillItems.Length == 0) return;

        var contentDialogResult = await this.ShowDialogAsync(GetLocalizedString("SkillsPage_DeleteSelectedSkillsDialogTitle"), GetFormattedString("SkillsPage_DeleteSelectedSkillsDialogMessage", selectedSkillItems.Length), GetLocalizedString("SkillsPage_DeleteButtonText"), GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        App.SkillService.DeleteSkills(selectedSkillItems);
        await ViewModel.ReloadSkillsAsync();
    }

    private async void OnExportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedSkillItems = ViewModel.FilteredSkills.Where(skillItem => skillItem.IsSelected).ToArray();
        if (selectedSkillItems.Length == 0)
        {
            await this.ShowDialogAsync(GetLocalizedString("SkillsPage_ExportBackupNoSelectionDialogTitle"), GetLocalizedString("SkillsPage_ExportBackupNoSelectionDialogMessage"));
            return;
        }

        var fileSavePicker = CreateBackupFileSavePicker(SelectedProviderKind);
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(GetLocalizedString("SkillsPage_ExportBackupLoadingMessage"));
        try { await App.SkillService.ExportSkillsAsync(SelectedProviderKind, selectedSkillItems, storageFile.Path); }
        finally { MainWindow.HideLoading(); }

        await this.ShowDialogAsync(GetLocalizedString("SkillsPage_ExportBackupDialogTitle"), GetLocalizedString("SkillsPage_ExportBackupDialogMessage"));
    }

    private async void OnImportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateBackupFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(GetLocalizedString("SkillsPage_ImportBackupLoadingMessage"));
        var importedCount = 0;
        try
        {
            importedCount = await App.SkillService.ImportSkillsAsync(SelectedProviderKind, storageFile.Path);
            await ViewModel.ReloadSkillsAsync();
        }
        finally { MainWindow.HideLoading(); }

        await this.ShowDialogAsync(GetLocalizedString("SkillsPage_ImportBackupDialogTitle"), GetFormattedString("SkillsPage_ImportBackupResultMessageFormat", importedCount));
    }

    private void OnRefreshSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.ReloadSkills();

    private void OnSelectAllSkillsCheckBoxChecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredSkillsSelection(true);

    private void OnSelectAllSkillsCheckBoxUnchecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredSkillsSelection(false);

    private void OnSkillsPageLoaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.ReloadSkills();

    private void OnSkillsPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private static FileOpenPicker CreateBackupFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker();
        fileOpenPicker.FileTypeFilter.Add(".caskills");
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        return fileOpenPicker;
    }

    private static FileSavePicker CreateBackupFileSavePicker(CliProviderKind providerKind)
    {
        var backupFileNamePrefix = SkillService.GetBackupFileNamePrefix(providerKind);
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{backupFileNamePrefix}-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}",
            DefaultFileExtension = ".caskills"
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(GetLocalizedString("SkillsPage_CaskillsBackupFileTypeChoice"), [".caskills"]);
        return fileSavePicker;
    }

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);

    private static string GetFormattedString(string resourceName, params object[] arguments) => App.LocalizationService.GetFormattedString(resourceName, arguments);
}
