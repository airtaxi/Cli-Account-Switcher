namespace CliAccountSwitcher.Api.Providers.ClaudeCode.Infrastructure;

public sealed class ClaudeCodePaths
{
    public string ConfigHomeDirectoryPath { get; }

    public string CredentialsFilePath { get; }

    public string GlobalConfigFilePath { get; }

    public ClaudeCodePaths()
    {
        ConfigHomeDirectoryPath = ResolveConfigHomeDirectoryPath();
        CredentialsFilePath = Path.Combine(ConfigHomeDirectoryPath, ".credentials.json");
        GlobalConfigFilePath = ResolveGlobalConfigFilePath(ConfigHomeDirectoryPath);
    }

    private static string ResolveConfigHomeDirectoryPath()
    {
        var claudeConfigDirectoryPath = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(claudeConfigDirectoryPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : Path.GetFullPath(claudeConfigDirectoryPath);
    }

    private static string ResolveGlobalConfigFilePath(string configHomeDirectoryPath)
    {
        var configHomeConfigFilePath = Path.Combine(configHomeDirectoryPath, ".config.json");
        if (File.Exists(configHomeConfigFilePath)) return configHomeConfigFilePath;

        var claudeConfigDirectoryPath = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(claudeConfigDirectoryPath)) return Path.Combine(configHomeDirectoryPath, ".claude.json");

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
    }
}
