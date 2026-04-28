namespace CliAccountSwitcher.WinUI.Models;

public sealed class ThirdPartyLicensePackage(string packageIdentifier, string versionText, string licenseText, string authorsText, string projectAddress)
{
    public string PackageIdentifier { get; set; } = packageIdentifier;

    public string VersionText { get; set; } = versionText;

    public string LicenseText { get; set; } = licenseText;

    public string AuthorsText { get; set; } = authorsText;

    public string ProjectAddress { get; set; } = projectAddress;
}
