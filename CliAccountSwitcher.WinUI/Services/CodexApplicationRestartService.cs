using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Services;

public sealed partial class CodexApplicationRestartService
{
    private const string CodexApplicationProcessName = "Codex";
    private const string CodexApplicationLowercaseProcessName = "codex";
    private const string CodexApplicationExecutableName = "Codex.exe";
    private const string DefaultCodexApplicationShellActivationAddress = "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App";
    private const string VisualStudioCodeProcessName = "Code";
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
        var runningVisualStudioCodeProcesses = GetRunningVisualStudioCodeProcesses();
        if (runningCodexApplicationProcesses.Count == 0 && runningVisualStudioCodeProcesses.Count == 0) return true;

        var executableFilePath = runningCodexApplicationProcesses.Select(TryGetProcessExecutableFilePath).FirstOrDefault(filePath => !string.IsNullOrWhiteSpace(filePath)) ?? "";
        var shellActivationAddress = CreateShellActivationAddress(executableFilePath);
        var visualStudioCodeExecutableFilePaths = GetRestartExecutableFilePaths(runningVisualStudioCodeProcesses);

        StopCodexApplicationProcesses(runningCodexApplicationProcesses);
        StopVisualStudioCodeProcesses(runningVisualStudioCodeProcesses);

        var wasCodexApplicationRestarted = runningCodexApplicationProcesses.Count == 0 || TryRestartCodexApplication(shellActivationAddress, executableFilePath);
        var wasVisualStudioCodeRestarted = runningVisualStudioCodeProcesses.Count == 0 || TryStartExecutableFilePaths(visualStudioCodeExecutableFilePaths);
        return wasCodexApplicationRestarted && wasVisualStudioCodeRestarted;
    }

    private static bool TryRestartCodexApplication(string shellActivationAddress, string executableFilePath)
    {
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

    private static IReadOnlyList<Process> GetRunningVisualStudioCodeProcesses()
    {
        var currentProcessIdentifier = Environment.ProcessId;
        var discoveredProcessIdentifiers = new HashSet<int>();
        var visualStudioCodeProcesses = new List<Process>();
        foreach (var visualStudioCodeProcess in Process.GetProcessesByName(VisualStudioCodeProcessName))
        {
            if (visualStudioCodeProcess.Id == currentProcessIdentifier || !IsVisualStudioCodeProcess(visualStudioCodeProcess) || !discoveredProcessIdentifiers.Add(visualStudioCodeProcess.Id))
            {
                visualStudioCodeProcess.Dispose();
                continue;
            }

            visualStudioCodeProcesses.Add(visualStudioCodeProcess);
        }

        return visualStudioCodeProcesses;
    }

    private static bool IsVisualStudioCodeProcess(Process process)
    {
        var executableFilePath = TryGetProcessExecutableFilePath(process);
        if (executableFilePath.EndsWith("\\Code.exe", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(process.ProcessName, VisualStudioCodeProcessName, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetRestartExecutableFilePaths(IReadOnlyList<Process> processes)
        => processes
            .Select(TryGetProcessExecutableFilePath)
            .Where(executableFilePath => !string.IsNullOrWhiteSpace(executableFilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private static void StopVisualStudioCodeProcesses(IReadOnlyList<Process> visualStudioCodeProcesses)
    {
        foreach (var visualStudioCodeProcess in visualStudioCodeProcesses)
        {
            try
            {
                if (visualStudioCodeProcess.HasExited) continue;
                visualStudioCodeProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }

        foreach (var visualStudioCodeProcess in visualStudioCodeProcesses)
        {
            try
            {
                if (!visualStudioCodeProcess.HasExited) visualStudioCodeProcess.WaitForExit(ProcessExitTimeoutMilliseconds);
            }
            catch { }
            finally { visualStudioCodeProcess.Dispose(); }
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
            var processStartInfo = new ProcessStartInfo
            {
                FileName = shellActivationAddress,
                UseShellExecute = true
            };

            Process.Start(processStartInfo);
            return true;
        }
        catch { return false; }
    }

    private static bool TryStartExecutableFilePaths(IReadOnlyList<string> executableFilePaths)
    {
        var wasAnyExecutableFilePathStarted = false;
        foreach (var executableFilePath in executableFilePaths)
        {
            if (TryStartExecutableFilePath(executableFilePath)) wasAnyExecutableFilePathStarted = true;
        }

        return wasAnyExecutableFilePathStarted;
    }

    private static bool TryStartExecutableFilePath(string executableFilePath)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executableFilePath,
                UseShellExecute = true
            };

            Process.Start(processStartInfo);
            return true;
        }
        catch { return false; }
    }

    [GeneratedRegex("^(?<packageName>.+)_\\d+\\.\\d+\\.\\d+\\.\\d+_[^_]+(?:_[^_]*)?__(?<publisherIdentifier>[a-z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsApplicationPackageFullNameRegex();
}
