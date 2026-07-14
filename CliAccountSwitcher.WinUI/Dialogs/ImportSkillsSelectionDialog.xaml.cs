using CliAccountSwitcher.WinUI.Services;
using CliAccountSwitcher.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed partial class ImportSkillsSelectionDialog : ContentDialog
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();

    public ImportSkillsSelectionDialog This => this;
    public ImportSkillsSelectionDialogViewModel ViewModel { get; }

    public ImportSkillsSelectionDialog()
    {
        ViewModel = App.Services.GetRequiredService<ImportSkillsSelectionDialogViewModel>();

        InitializeComponent();
        _applicationThemeService.ApplyThemeToElement(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
    }

    private void OnSelectAllSkillsCheckBoxChecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetAllSelection(true);

    private void OnSelectAllSkillsCheckBoxUnchecked(object sender, RoutedEventArgs routedEventArguments) => ViewModel.SetAllSelection(false);

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToElement(this);

    private void OnImportSkillsSelectionDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs contentDialogClosedEventArguments) => _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
}