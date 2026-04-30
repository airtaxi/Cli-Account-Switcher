using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Models;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Services;

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

    public async Task ExportSettingsAsync(string applicationSettingsFilePath, CancellationToken cancellationToken = default)
    {
        NormalizeSettings(Settings);
        await using var applicationSettingsFileStream = File.Create(applicationSettingsFilePath);
        await JsonSerializer.SerializeAsync(applicationSettingsFileStream, Settings, CodexAccountJsonSerializerContext.Default.ApplicationSettings, cancellationToken);
    }

    public async Task ImportSettingsAsync(string applicationSettingsFilePath, CancellationToken cancellationToken = default)
    {
        await using var applicationSettingsFileStream = File.OpenRead(applicationSettingsFilePath);
        var importedApplicationSettings = await JsonSerializer.DeserializeAsync(applicationSettingsFileStream, CodexAccountJsonSerializerContext.Default.ApplicationSettings, cancellationToken) ?? throw new InvalidDataException();
        NormalizeSettings(importedApplicationSettings);
        CopySettings(importedApplicationSettings, Settings);
        await SaveSettingsAsync(cancellationToken);
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
        applicationSettings.SelectedProviderKind = NormalizeSelectedProviderKind(applicationSettings.SelectedProviderKind);
        applicationSettings.ActiveAccountUsageRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(applicationSettings.ActiveAccountUsageRefreshIntervalSeconds, ApplicationSettings.DefaultActiveAccountUsageRefreshIntervalSeconds);
        applicationSettings.InactiveAccountUsageRefreshIntervalSeconds = NormalizeRefreshIntervalSeconds(applicationSettings.InactiveAccountUsageRefreshIntervalSeconds, ApplicationSettings.DefaultInactiveAccountUsageRefreshIntervalSeconds);
        applicationSettings.PrimaryUsageWarningThresholdPercentage = NormalizePercentage(applicationSettings.PrimaryUsageWarningThresholdPercentage);
        applicationSettings.SecondaryUsageWarningThresholdPercentage = NormalizePercentage(applicationSettings.SecondaryUsageWarningThresholdPercentage);
    }

    private static void CopySettings(ApplicationSettings sourceApplicationSettings, ApplicationSettings destinationApplicationSettings)
    {
        destinationApplicationSettings.SchemaVersion = sourceApplicationSettings.SchemaVersion;
        destinationApplicationSettings.Theme = sourceApplicationSettings.Theme;
        destinationApplicationSettings.LanguageOverride = sourceApplicationSettings.LanguageOverride;
        destinationApplicationSettings.SelectedProviderKind = sourceApplicationSettings.SelectedProviderKind;
        destinationApplicationSettings.IsAutomaticUpdateCheckEnabled = sourceApplicationSettings.IsAutomaticUpdateCheckEnabled;
        destinationApplicationSettings.IsStartupLaunchEnabled = sourceApplicationSettings.IsStartupLaunchEnabled;
        destinationApplicationSettings.IsExpiredAccountAutomaticDeletionEnabled = sourceApplicationSettings.IsExpiredAccountAutomaticDeletionEnabled;
        destinationApplicationSettings.IsExpiredAccountNotificationEnabled = sourceApplicationSettings.IsExpiredAccountNotificationEnabled;
        destinationApplicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled = sourceApplicationSettings.IsActiveAccountUsageAutomaticRefreshEnabled;
        destinationApplicationSettings.ActiveAccountUsageRefreshIntervalSeconds = sourceApplicationSettings.ActiveAccountUsageRefreshIntervalSeconds;
        destinationApplicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled = sourceApplicationSettings.IsInactiveAccountUsageAutomaticRefreshEnabled;
        destinationApplicationSettings.InactiveAccountUsageRefreshIntervalSeconds = sourceApplicationSettings.InactiveAccountUsageRefreshIntervalSeconds;
        destinationApplicationSettings.PrimaryUsageWarningThresholdPercentage = sourceApplicationSettings.PrimaryUsageWarningThresholdPercentage;
        destinationApplicationSettings.SecondaryUsageWarningThresholdPercentage = sourceApplicationSettings.SecondaryUsageWarningThresholdPercentage;
        destinationApplicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled = sourceApplicationSettings.IsPrimaryUsageLowQuotaNotificationEnabled;
        destinationApplicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled = sourceApplicationSettings.IsSecondaryUsageLowQuotaNotificationEnabled;
    }

    private static string NormalizeLanguageOverride(string languageOverride) => languageOverride is "ko-KR" or "en-US" or "ja-JP" or "zh-Hans" or "zh-Hant" ? languageOverride : "";

    private static CliProviderKind NormalizeSelectedProviderKind(CliProviderKind selectedProviderKind) => selectedProviderKind is CliProviderKind.Codex or CliProviderKind.ClaudeCode ? selectedProviderKind : CliProviderKind.Codex;

    private static int NormalizeRefreshIntervalSeconds(int refreshIntervalSeconds, int defaultRefreshIntervalSeconds) => refreshIntervalSeconds <= 0 ? defaultRefreshIntervalSeconds : Math.Clamp(refreshIntervalSeconds, 60, 86400);

    private static int NormalizePercentage(int percentage) => Math.Clamp(percentage, 0, 100);
}
