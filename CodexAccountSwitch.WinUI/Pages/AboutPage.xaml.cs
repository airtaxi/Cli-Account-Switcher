using CodexAccountSwitch.WinUI.Dialogs;
using CodexAccountSwitch.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;

namespace CodexAccountSwitch.WinUI.Pages;

public sealed partial class AboutPage : Page
{
    private const string ApplicationRepositoryAddress = "https://github.com/airtaxi/CodexAccountSwitch.WinUI";
    private const string CreatorGitHubAddress = "https://github.com/airtaxi";
    private const string InspirationRepositoryAddress = "https://github.com/isxlan0/Codex_AccountSwitch";

    private static readonly List<ThirdPartyLicensePackage> s_thirdPartyLicensePackages = CreateThirdPartyLicensePackages();

    public string ApplicationVersionText { get; } = GetCurrentApplicationVersion();

    public string CopyrightText { get; } = "Copyright (c) 2026 Codex Account Switch WinUI contributors";

#pragma warning disable CA1822 // Mark members as static => Used in XAML binding, which doesn't support static members
    public List<ThirdPartyLicensePackage> ThirdPartyLicensePackages => s_thirdPartyLicensePackages;
#pragma warning restore CA1822 // Mark members as static => Used in XAML binding, which doesn't support static members

    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OnGitHubRepositoryButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await OpenAddressAsync(ApplicationRepositoryAddress);

    private async void OnCreatorGitHubButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await OpenAddressAsync(CreatorGitHubAddress);

    private async void OnInspirationRepositoryButtonClicked(object sender, RoutedEventArgs routedEventArguments) => await OpenAddressAsync(InspirationRepositoryAddress);

    private async void OnThirdPartyLicensesButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        var thirdPartyLicensesDialog = new ThirdPartyLicensesDialog(ThirdPartyLicensePackages) { XamlRoot = XamlRoot };

        await thirdPartyLicensesDialog.ShowAsync();
    }

    private static async Task OpenAddressAsync(string address) => await Launcher.LaunchUriAsync(new Uri(address));

    private static List<ThirdPartyLicensePackage> CreateThirdPartyLicensePackages()
    {
        var windowsSoftwareDevelopmentKitLicenseText = GetLocalizedString("ThirdPartyLicensePackage_WindowsSoftwareDevelopmentKitLicense");

        return
        [
            new("CommunityToolkit.Mvvm", "8.4.0", "MIT", "Microsoft", "https://github.com/CommunityToolkit/dotnet"),
            new("CommunityToolkit.WinUI.Converters", "8.2.251219", "MIT", "Microsoft.Toolkit", "https://github.com/CommunityToolkit/Windows"),
            new("DevWinUI.Controls", "9.9.4", "MIT", "Mahdi Hosseini", "https://github.com/ghost1372/DevWinUI"),
            new("Microsoft.Windows.SDK.BuildTools", "10.0.28000.1721", windowsSoftwareDevelopmentKitLicenseText, "Microsoft", "https://aka.ms/WinSDKProjectURL"),
            new("Microsoft.WindowsAppSDK", "1.8.260416003", "MIT", "Microsoft", "https://github.com/microsoft/windowsappsdk"),
            new("WinUIEx", "2.9.0", "MIT", "Morten Nielsen", "https://dotmorten.github.io/WinUIEx")
        ];
    }

    private static string FormatCurrentApplicationVersion(PackageVersion packageVersion) => $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";

    public static string GetCurrentApplicationVersion() => FormatCurrentApplicationVersion(Package.Current.Id.Version);

    private static string GetLocalizedString(string resourceName) => App.LocalizationService.GetLocalizedString(resourceName);
}
