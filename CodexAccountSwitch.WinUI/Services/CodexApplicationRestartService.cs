using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodexAccountSwitch.WinUI.Services;

public sealed partial class CodexApplicationRestartService
{
    private const string CodexApplicationProcessName = "Codex";
    private const string CodexApplicationLowercaseProcessName = "codex";
    private const string CodexApplicationExecutableName = "Codex.exe";
    private const string DefaultCodexApplicationShellActivationAddress = "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App";
    private const int ProcessExitTimeoutMilliseconds = 5000;
    private static readonly string[] s_codexApplicationProcessNames = [CodexApplicationProcessName, CodexApplicationLowercaseProcessName];

    public Task<bool> RestartCodexApplicationAsync() => Task.Run(TryRestartCodexApplication);

    private static bool TryRestartCodexApplication()
    {
        try { return RestartCodexApplication(); }
        catch { return false; }
    }

    private static bool RestartCodexApplication()
    {
        var runningCodexApplicationProcesses = GetRunningCodexApplicationProcesses();
        var executableFilePath = runningCodexApplicationProcesses.Select(TryGetProcessExecutableFilePath).FirstOrDefault(filePath => !string.IsNullOrWhiteSpace(filePath));
        var shellActivationAddress = CreateShellActivationAddress(executableFilePath);

        StopCodexApplicationProcesses(runningCodexApplicationProcesses);

        if (!string.IsNullOrWhiteSpace(shellActivationAddress) && TryStartShellActivationAddress(shellActivationAddress)) return true;
        if (!string.IsNullOrWhiteSpace(executableFilePath) && TryStartExecutableFilePath(executableFilePath)) return true;
        if (TryStartShellActivationAddress(DefaultCodexApplicationShellActivationAddress)) return true;
        return TryStartExecutableFilePath(CodexApplicationExecutableName);
    }

    private static IReadOnlyList<Process> GetRunningCodexApplicationProcesses()
    {
        var currentProcessIdentifier = Environment.ProcessId;
        var discoveredProcessIdentifiers = new HashSet<int>();
        var codexApplicationProcesses = new List<Process>();
        foreach (var codexApplicationProcessName in s_codexApplicationProcessNames)
        {
            foreach (var codexApplicationProcess in Process.GetProcessesByName(codexApplicationProcessName))
            {
                if (codexApplicationProcess.Id == currentProcessIdentifier || !IsCodexApplicationProcess(codexApplicationProcess) || !discoveredProcessIdentifiers.Add(codexApplicationProcess.Id))
                {
                    codexApplicationProcess.Dispose();
                    continue;
                }

                codexApplicationProcesses.Add(codexApplicationProcess);
            }
        }

        return codexApplicationProcesses;
    }

    private static bool IsCodexApplicationProcess(Process process)
    {
        var executableFilePath = TryGetProcessExecutableFilePath(process);
        if (executableFilePath.Contains("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(process.ProcessName, CodexApplicationProcessName, StringComparison.Ordinal);
    }

    private static void StopCodexApplicationProcesses(IReadOnlyList<Process> codexApplicationProcesses)
    {
        foreach (var codexApplicationProcess in codexApplicationProcesses)
        {
            try
            {
                if (codexApplicationProcess.HasExited) continue;
                codexApplicationProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }

        foreach (var codexApplicationProcess in codexApplicationProcesses)
        {
            try
            {
                if (!codexApplicationProcess.HasExited) codexApplicationProcess.WaitForExit(ProcessExitTimeoutMilliseconds);
            }
            catch { }
            finally { codexApplicationProcess.Dispose(); }
        }
    }

    private static string TryGetProcessExecutableFilePath(Process process)
    {
        try { return process.MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    private static string CreateShellActivationAddress(string executableFilePath)
    {
        var packageFamilyName = TryGetPackageFamilyName(executableFilePath);
        return string.IsNullOrWhiteSpace(packageFamilyName) ? "" : $"shell:AppsFolder\\{packageFamilyName}!App";
    }

    private static string TryGetPackageFamilyName(string executableFilePath)
    {
        if (string.IsNullOrWhiteSpace(executableFilePath)) return "";

        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(executableFilePath) ?? "");
        while (directoryInfo is not null)
        {
            var packageFullNameMatch = WindowsApplicationPackageFullNameRegex().Match(directoryInfo.Name);
            if (packageFullNameMatch.Success) return $"{packageFullNameMatch.Groups["packageName"].Value}_{packageFullNameMatch.Groups["publisherIdentifier"].Value}";
            directoryInfo = directoryInfo.Parent;
        }

        return "";
    }

    private static bool TryStartShellActivationAddress(string shellActivationAddress)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = shellActivationAddress,
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
    }

    private static bool TryStartExecutableFilePath(string executableFilePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executableFilePath,
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
    }

    [GeneratedRegex("^(?<packageName>.+)_\\d+\\.\\d+\\.\\d+\\.\\d+_[^_]+(?:_[^_]*)?__(?<publisherIdentifier>[a-z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsApplicationPackageFullNameRegex();
}
