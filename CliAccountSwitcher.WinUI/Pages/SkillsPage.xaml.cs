using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using CliAccountSwitcher.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly SkillService _skillService = App.Services.GetRequiredService<SkillService>();

    public SkillsPageViewModel ViewModel { get; }

    public SkillsPage()
    {
        ViewModel = App.Services.GetRequiredService<SkillsPageViewModel>();
        InitializeComponent();
    }

    private CliProviderKind SelectedProviderKind => ViewModel.SelectedProviderKind;

    private async void OnDeleteSelectedSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedSkillItems = ViewModel.Skills.Where(skillItem => skillItem.IsSelected).ToArray();
        if (selectedSkillItems.Length == 0) return;

        var contentDialogResult = await this.ShowDialogAsync(_localizationService.GetLocalizedString("SkillsPage_DeleteSelectedSkillsDialogTitle"), _localizationService.GetFormattedString("SkillsPage_DeleteSelectedSkillsDialogMessage", selectedSkillItems.Length), _localizationService.GetLocalizedString("SkillsPage_DeleteButtonText"), _localizationService.GetLocalizedString("DialogHelper_CancelButtonText"));
        if (contentDialogResult != ContentDialogResult.Primary) return;

        _skillService.DeleteSkills(selectedSkillItems);
        await ViewModel.ReloadSkillsAsync();
    }

    private async void OnExportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var selectedSkillItems = ViewModel.Skills.Where(skillItem => skillItem.IsSelected).ToArray();
        if (selectedSkillItems.Length == 0)
        {
            await this.ShowDialogAsync(_localizationService.GetLocalizedString("SkillsPage_ExportBackupNoSelectionDialogTitle"), _localizationService.GetLocalizedString("SkillsPage_ExportBackupNoSelectionDialogMessage"));
            return;
        }

        var fileSavePicker = CreateBackupFileSavePicker(SelectedProviderKind);
        var storageFile = await fileSavePicker.PickSaveFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("SkillsPage_ExportBackupLoadingMessage"));
        try { await _skillService.ExportSkillsAsync(SelectedProviderKind, selectedSkillItems, storageFile.Path); }
        finally { MainWindow.HideLoading(); }

        await this.ShowDialogAsync(_localizationService.GetLocalizedString("SkillsPage_ExportBackupDialogTitle"), _localizationService.GetLocalizedString("SkillsPage_ExportBackupDialogMessage"));
    }

    private async void OnImportSkillsBackupButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var fileOpenPicker = CreateBackupFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        MainWindow.ShowLoading(_localizationService.GetLocalizedString("SkillsPage_ImportBackupLoadingMessage"));
        var importedCount = 0;
        try
        {
            importedCount = await _skillService.ImportSkillsAsync(SelectedProviderKind, storageFile.Path);
            await ViewModel.ReloadSkillsAsync();
        }
        finally { MainWindow.HideLoading(); }

        await this.ShowDialogAsync(_localizationService.GetLocalizedString("SkillsPage_ImportBackupDialogTitle"), _localizationService.GetFormattedString("SkillsPage_ImportBackupResultMessageFormat", importedCount));
    }

    private void OnRefreshSkillsButtonClicked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.ReloadSkills();

    private void OnSkillSearchAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs autoSuggestBoxTextChangedEventArguments) => ViewModel.SearchText = sender.Text;

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

    private FileSavePicker CreateBackupFileSavePicker(CliProviderKind providerKind)
    {
        var backupFileNamePrefix = SkillService.GetBackupFileNamePrefix(providerKind);
        var fileSavePicker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{backupFileNamePrefix}-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}",
            DefaultFileExtension = ".caskills"
        };
        InitializeWithWindow.Initialize(fileSavePicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileSavePicker.FileTypeChoices.Add(_localizationService.GetLocalizedString("SkillsPage_CaskillsBackupFileTypeChoice"), [".caskills"]);
        return fileSavePicker;
    }


}
