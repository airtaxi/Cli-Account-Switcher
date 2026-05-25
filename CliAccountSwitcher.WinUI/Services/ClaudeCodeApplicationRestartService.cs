using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ClaudeCodeApplicationRestartService
{
    private const string VisualStudioCodeProcessName = "Code";
    private const int ProcessExitTimeoutMilliseconds = 5000;

    public Task<bool> RestartClaudeCodeAsync() => Task.Run(TryRestartClaudeCode);

    private static bool TryRestartClaudeCode()
    {
        try { return RestartClaudeCode(); }
        catch { return false; }
    }

    private static bool RestartClaudeCode()
    {
        var runningVisualStudioCodeProcesses = GetRunningVisualStudioCodeProcesses();
        if (runningVisualStudioCodeProcesses.Count == 0) return true;

        var executableFilePaths = GetRestartExecutableFilePaths(runningVisualStudioCodeProcesses);

        StopVisualStudioCodeProcesses(runningVisualStudioCodeProcesses);

        return TryStartExecutableFilePaths(executableFilePaths);
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

    private static IReadOnlyList<string> GetRestartExecutableFilePaths(IReadOnlyList<Process> visualStudioCodeProcesses)
        => visualStudioCodeProcesses
            .Select(TryGetProcessExecutableFilePath)
            .Where(executableFilePath => !string.IsNullOrWhiteSpace(executableFilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
}
