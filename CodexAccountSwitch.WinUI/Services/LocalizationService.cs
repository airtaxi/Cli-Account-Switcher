using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexAccountSwitch.WinUI.Services;

public sealed class LocalizationService
{
    private static readonly List<string> s_supportedLanguageTags = ["en-US", "ko-KR"];
    private ResourceLoader _resourceLoader;

    public event Action LanguageChanged;

    public string CurrentLanguageTag { get; private set; } = "";

    public LocalizationService(string languageTag) => ApplyLanguageTag(languageTag);

    public void ApplyLanguageTag(string languageTag)
    {
        var resolvedLanguageTag = ResolveSupportedLanguageTag(languageTag);
        if (CurrentLanguageTag == resolvedLanguageTag && string.Equals(ApplicationLanguages.PrimaryLanguageOverride, resolvedLanguageTag, StringComparison.Ordinal)) return;

        ApplicationLanguages.PrimaryLanguageOverride = resolvedLanguageTag;
        ApplyCurrentThreadCultures(resolvedLanguageTag);

        _resourceLoader = new ResourceLoader();
        CurrentLanguageTag = resolvedLanguageTag;

        LanguageChanged?.Invoke();
    }

    public string GetFormattedString(string resourceName, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, GetLocalizedString(resourceName), arguments);

    public string GetLocalizedString(string resourceName)
    {
        var normalizedResourceName = resourceName.Replace('.', '/');
        string localizedString;
        try { localizedString = _resourceLoader.GetString(normalizedResourceName); }
        catch (COMException) { localizedString = resourceName; }

        return string.IsNullOrWhiteSpace(localizedString) ? resourceName : localizedString;
    }

    private static void ApplyCurrentThreadCultures(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return;

        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(languageTag);
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        }
        catch (CultureNotFoundException) { }
    }

    private static string ResolveSupportedLanguageTag(string languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag) && s_supportedLanguageTags.Contains(languageTag)) return languageTag;

        var installedUserInterfaceCultureName = CultureInfo.InstalledUICulture.Name;
        return s_supportedLanguageTags.Contains(installedUserInterfaceCultureName) ? installedUserInterfaceCultureName : s_supportedLanguageTags[0];
    }
}
