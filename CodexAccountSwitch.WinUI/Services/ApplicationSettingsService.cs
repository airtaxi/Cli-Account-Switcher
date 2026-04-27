using CodexAccountSwitch.WinUI.Helpers;
using CodexAccountSwitch.WinUI.Models;
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

    public ApplicationSettings Settings { get; }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Constants.UserDataDirectory);
        await using var configurationFileStream = File.Create(Constants.ConfigurationFilePath);
        await JsonSerializer.SerializeAsync(configurationFileStream, Settings, CodexAccountJsonSerializerContext.Default.ApplicationSettings, cancellationToken);
    }

    private static ApplicationSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(Constants.ConfigurationFilePath)) return new ApplicationSettings();

            using var configurationFileStream = File.OpenRead(Constants.ConfigurationFilePath);
            return JsonSerializer.Deserialize(configurationFileStream, CodexAccountJsonSerializerContext.Default.ApplicationSettings) ?? new ApplicationSettings();
        }
        catch { return new ApplicationSettings(); }
    }
}
