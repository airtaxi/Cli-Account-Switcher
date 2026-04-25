using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexAccountSwitch.Api.Models;

namespace CodexAccountSwitch.Api.Infrastructure;

public sealed class CodexClientMetadataProvider(CodexApiClientOptions codexApiClientOptions)
{
    public string ReadCodexVersion()
    {
        var versionFilePath = Path.Combine(codexApiClientOptions.CodexHomeDirectoryPath, "version.json");
        if (!File.Exists(versionFilePath)) return codexApiClientOptions.DefaultCodexVersion;

        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(versionFilePath));
            if (jsonDocument.RootElement.TryGetProperty("latest_version", out var latestVersionElement) && latestVersionElement.GetString() is { Length: > 0 } latestVersion) return latestVersion;
        }
        catch { } // Ignore any exceptions and fall back to the default version.

        return codexApiClientOptions.DefaultCodexVersion;
    }

    public string BuildCodexApiUserAgent() => $"codex_cli_rs/{ReadCodexVersion()} (Windows {BuildOperatingSystemVersionTag()}; {BuildProcessorArchitectureTag()})";

    public string BuildUsageUserAgent() => $"codex_vscode/{ReadCodexVersion()} (Windows {BuildOperatingSystemVersionTag()}; {BuildProcessorArchitectureTag()}) unknown (VS Code; {codexApiClientOptions.DefaultVisualStudioCodeExtensionVersion})";

    private static string BuildOperatingSystemVersionTag() => Environment.OSVersion.Version.ToString();

    private static string BuildProcessorArchitectureTag() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => "unknown"
    };
}
