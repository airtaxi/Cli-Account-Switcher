using CodexAccountSwitch.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windows.System;

namespace CodexAccountSwitch.WinUI.Dialogs;

public sealed partial class ThirdPartyLicensesDialog : ContentDialog
{
    public ThirdPartyLicensesDialog This => this;
    public List<ThirdPartyLicensePackage> ThirdPartyLicensePackages { get; }

    public ThirdPartyLicensesDialog(List<ThirdPartyLicensePackage> thirdPartyLicensePackages)
    {
        ThirdPartyLicensePackages = thirdPartyLicensePackages;

        InitializeComponent();
    }

    private async void OnPackageProjectButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (sender is not Button button || button.Tag is not string projectAddress || string.IsNullOrWhiteSpace(projectAddress)) return;

        await Launcher.LaunchUriAsync(new Uri(projectAddress));
    }
}
