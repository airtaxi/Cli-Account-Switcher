using System;
using System.IO;

namespace CliAccountSwitcher.WinUI;

public static class Constants
{
    public static string CodexHomeDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string CodexSkillsDirectory => Path.Combine(CodexHomeDirectory, "skills");
    public static string CurrentAuthenticationFilePath => Path.Combine(CodexHomeDirectory, "auth.json");
    public static string ClaudeCodeHomeDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static string ClaudeCodeSkillsDirectory => Path.Combine(ClaudeCodeHomeDirectory, "skills");
    public static string UserDataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountSwitch.WinUI");
    public static string BackupsDirectory => Path.Combine(UserDataDirectory, "backups");
    public static string ProviderSnapshotsDirectory => Path.Combine(UserDataDirectory, "provider-snapshots");
    public static string ConfigurationFilePath => Path.Combine(UserDataDirectory, "config.json");
    public static string AccountsFilePath => Path.Combine(UserDataDirectory, "accounts.json");
    public static string ZaiAccountsFilePath => Path.Combine(UserDataDirectory, "zai-accounts.json");
    public static string OpenCodeGoHomeDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode");
    public static string OpenCodeGoSkillsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "skills");
    public static string OpenCodeGoAuthFilePath => Path.Combine(OpenCodeGoHomeDirectory, "auth.json");
    public static string OpenCodeGoExecutableFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "@opencode-aidesktop", "OpenCode.exe");
    public static string OpenCodeGoAccountsFilePath => Path.Combine(UserDataDirectory, "opencode-accounts.json");
    public static string OllamaAccountsFilePath => Path.Combine(UserDataDirectory, "ollama-accounts.json");
}
