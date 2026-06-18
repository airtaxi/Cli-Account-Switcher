using CliAccountSwitcher.Api.Providers.Zai.Models;

namespace CliAccountSwitcher.Api.Providers.Zai.Infrastructure;

public static class ZaiChelperConfigReader
{
    public static Task<ZaiChelperConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configFilePath = new ZaiChelperConfigPaths().ConfigFilePath;
        return LoadAsync(configFilePath, cancellationToken);
    }

    public static async Task<ZaiChelperConfig> LoadAsync(string configFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configFilePath)) throw new FileNotFoundException("The chelper config file was not found.", configFilePath);
        var configText = await File.ReadAllTextAsync(configFilePath, cancellationToken);
        return ZaiChelperConfig.Parse(configText);
    }
}
