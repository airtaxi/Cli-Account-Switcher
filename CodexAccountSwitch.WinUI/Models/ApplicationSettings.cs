using Microsoft.UI.Xaml;

namespace CodexAccountSwitch.WinUI.Models;

public sealed class ApplicationSettings
{
    public ElementTheme Theme { get; set; } = ElementTheme.Default;

    public string LanguageOverride { get; set; } = null;
}
