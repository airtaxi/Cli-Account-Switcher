using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Pages;

public sealed partial class SkillsPage : Page
{
    public SkillsPageViewModel ViewModel { get; }

    public SkillsPage()
    {
        ViewModel = App.Services.GetRequiredService<SkillsPageViewModel>();
        InitializeComponent();
    }

    private async void OnDeleteSelectedSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (!ViewModel.HasSelectedSkills) return;

        var contentDialogResult = await ShowDialogAsync(ViewModel.CreateDeleteSelectedSkillsConfirmationDialogData());
        if (contentDialogResult != ContentDialogResult.Primary) return;

        ViewModel.DeleteSelectedSkills();
    }

    private async void OnExportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = ViewModel.SelectedProviderKind;
        var selectedSkillItems = ViewModel.CreateSelectedSkillItemsSnapshot(selectedProviderKind);
        if (selectedSkillItems.Length == 0)
        {
            await ShowDialogAsync(ViewModel.CreateExportBackupNoSelectionDialogData());
            return;
        }

        var fileSavePicker = CreateBackupFileSavePicker(selectedProviderKind);
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(ViewModel.ExportBackupLoadingMessage);
        BasicDialogData dialogData;
        try { dialogData = await ViewModel.ExportSelectedSkillsAsync(selectedProviderKind, selectedSkillItems, storageFile.Path); }
        finally { MainWindow.HideLoading(); }

        await ShowDialogAsync(dialogData);
    }

    private async void OnImportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedProviderKind = ViewModel.SelectedProviderKind;
        var fileOpenPicker = CreateBackupFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(ViewModel.ImportBackupLoadingMessage);
        BasicDialogData dialogData;
        try { dialogData = await ViewModel.ImportSkillsBackupAsync(selectedProviderKind, storageFile.Path); }
        finally { MainWindow.HideLoading(); }

        await ShowDialogAsync(dialogData);
    }

    private void OnRefreshSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.ReloadSkills();

    private void OnSelectAllSkillsCheckBoxChecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredSkillsSelection(true);

    private void OnSelectAllSkillsCheckBoxUnchecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetFilteredSkillsSelection(false);

    private void OnSkillsPageLoaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.ReloadSkills();

    private void OnSkillsPageUnloaded(object sender, RoutedEventArgs routedEventArguments) => ViewModel.Dispose();

    private async Task<ContentDialogResult> ShowDialogAsync(BasicDialogData dialogData) => await this.ShowDialogAsync(dialogData.Title, dialogData.Message, dialogData.PrimaryButtonText, dialogData.SecondaryButtonText);

    private FileOpenPicker CreateBackupFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileOpenPicker.FileTypeFilter.Add(ViewModel.BackupFileExtension);
        fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        return fileOpenPicker;
    }

    private FileSavePicker CreateBackupFileSavePicker(CliProviderKind providerKind)
    {
        var backupFileExtension = ViewModel.BackupFileExtension;
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        fileSavePicker.FileTypeChoices.Add(ViewModel.BackupFileTypeChoiceText, [backupFileExtension]);
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        fileSavePicker.SuggestedFileName = ViewModel.GetBackupSuggestedFileName(providerKind);
        fileSavePicker.DefaultFileExtension = backupFileExtension;
        return fileSavePicker;
    }


}
