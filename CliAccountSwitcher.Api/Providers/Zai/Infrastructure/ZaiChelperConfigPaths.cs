namespace CliAccountSwitcher.Api.Providers.Zai.Infrastructure;

public sealed class ZaiChelperConfigPaths
{
    public string ConfigDirectoryPath { get; }

    public string ConfigFilePath { get; }

    public ZaiChelperConfigPaths()
    {
        ConfigDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".chelper");
        ConfigFilePath = Path.Combine(ConfigDirectoryPath, "config.yaml");
    }
}
