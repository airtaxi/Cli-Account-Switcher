using System;
using System.IO;

namespace CodexAccountSwitch.WinUI;

public static class Constants
{
    public static string CodexHomeDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string CurrentAuthenticationFilePath => Path.Combine(CodexHomeDirectory, "auth.json");
    public static string UserDataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountSwitch.WinUI");
    public static string BackupsDirectory => Path.Combine(UserDataDirectory, "backups");
    public static string ConfigurationFilePath => Path.Combine(UserDataDirectory, "config.json");
    public static string AccountsFilePath => Path.Combine(UserDataDirectory, "accounts.json");
}
