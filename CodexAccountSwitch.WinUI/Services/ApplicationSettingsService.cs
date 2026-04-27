using CodexAccountSwitch.WinUI.Helpers;
using CodexAccountSwitch.WinUI.Models;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountSwitch.WinUI.Services;

public sealed class ApplicationSettingsService
{
    public ApplicationSettingsService()
    {
        Settings = LoadSettings();
    }

    public event EventHandler SettingsChanged;

    public ApplicationSettings Settings { get; }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        NormalizeSettings(Settings);
        Directory.CreateDirectory(Constants.UserDataDirectory);
        await using var configurationFileStream = File.Create(Constants.ConfigurationFilePath);
        await JsonSerializer.SerializeAsync(configurationFileStream, Settings, CodexAccountJsonSerializerContext.Default.ApplicationSettings, cancellationToken);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ApplicationSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(Constants.ConfigurationFilePath)) return new ApplicationSettings();

            using var configurationFileStream = File.OpenRead(Constants.ConfigurationFilePath);
            var applicationSettings = JsonSerializer.Deserialize(configurationFileStream, CodexAccountJsonSerializerContext.Default.ApplicationSettings) ?? new ApplicationSettings();
            NormalizeSettings(applicationSettings);
            return applicationSettings;
        }
        catch { return new ApplicationSettings(); }
    }

    private static void NormalizeSettings(ApplicationSettings applicationSettings)
    {
        applicationSettings.SchemaVersion = ApplicationSettings.CurrentSchemaVersion;
        applicationSettings.Theme = applicationSettings.Theme is ElementTheme.Default or ElementTheme.Light or ElementTheme.Dark ? applicationSettings.Theme : ElementTheme.Default;
        applicationSettings.LanguageOverride = NormalizeLanguageOverride(applicationSettings.LanguageOverride);
        applicationSettings.ActiveAccountUsageRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds, ApplicationSettings.DefaultActiveAccountUsageRefreshIntervalSeconds);
        applicationSettings.InactiveAccountUsageRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds, ApplicationSettings.DefaultInactiveAccountUsageRefreshIntervalSeconds);
        applicationSettings.PrimaryUsageWarningThresholdPercentage = NormalizePercentage(applicationSettings.PrimaryUsageWarningThresholdPercentage);
        applicationSettings.SecondaryUsageWarningThresholdPercentage = NormalizePercentage(applicationSettings.SecondaryUsageWarningThresholdPercentage);
    }

    private static string NormalizeLanguageOverride(string languageOverride) => languageOverride is "ko-KR" or "en-US" or "ja-JP" or "zh-Hans" or "zh-Hant" ? languageOverride : "";

    private static int NormalizeRefreshIntervalSeconds(int refreshIntervalSeconds, int defaultRefreshIntervalSeconds) => refreshIntervalSeconds <= 0 ? defaultRefreshIntervalSeconds : Math.Clamp(refreshIntervalSeconds, 60, 86400);

    private static int NormalizePercentage(int percentage) => Math.Clamp(percentage, 0, 100);
}
