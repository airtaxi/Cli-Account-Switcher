using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windows.System;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed partial class ThirdPartyLicensesDialog : ContentDialog
{
    private readonly ApplicationThemeService _applicationThemeService = App.Services.GetRequiredService<ApplicationThemeService>();

    public ThirdPartyLicensesDialog This => this;
    public List<ThirdPartyLicensePackage> ThirdPartyLicensePackages { get; }

    public ThirdPartyLicensesDialog(List<ThirdPartyLicensePackage> thirdPartyLicensePackages)
    {
        ThirdPartyLicensePackages = thirdPartyLicensePackages;

        InitializeComponent();
        _applicationThemeService.ApplyThemeToElement(this);
        _applicationThemeService.ThemeChanged += OnApplicationThemeServiceThemeChanged;
    }

    private async void OnPackageProjectButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (sender is not Button button || button.Tag is not string projectAddress || string.IsNullOrWhiteSpace(projectAddress)) return;

        await Launcher.LaunchUriAsync(new Uri(projectAddress));
    }

    private void OnApplicationThemeServiceThemeChanged(ElementTheme theme) => _applicationThemeService.ApplyThemeToElement(this);

    private void OnThirdPartyLicensesDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs contentDialogClosedEventArguments) => _applicationThemeService.ThemeChanged -= OnApplicationThemeServiceThemeChanged;
}
