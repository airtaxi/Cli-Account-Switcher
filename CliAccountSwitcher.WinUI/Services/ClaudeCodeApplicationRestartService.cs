using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class ClaudeCodeApplicationRestartService
{
    private const string ClaudeCodeApplicationProcessName = "Claude";
    private const string ClaudeCodeCliProcessName = "claude";
    private const string ClaudeCodeCliExecutableName = "claude.exe";
    private const string DefaultClaudeCodeDesktopProtocolAddress = "claude://code/new";
    private const int ProcessExitTimeoutMilliseconds = 5000;
    private static readonly string[] s_claudeCodeProcessNames = [ClaudeCodeApplicationProcessName, ClaudeCodeCliProcessName];

    public Task<bool> RestartClaudeCodeAsync() => Task.Run(TryRestartClaudeCode);

    private static bool TryRestartClaudeCode()
    {
        try { return RestartClaudeCode(); }
        catch { return false; }
    }

    private static bool RestartClaudeCode()
    {
        var runningClaudeCodeProcesses = GetRunningClaudeCodeProcesses();
        var executableFilePaths = GetRestartExecutableFilePaths(runningClaudeCodeProcesses);

        StopClaudeCodeProcesses(runningClaudeCodeProcesses);

        if (TryStartExecutableFilePaths(executableFilePaths)) return true;
        if (TryStartProtocolAddress(DefaultClaudeCodeDesktopProtocolAddress)) return true;
        return TryStartExecutableFilePath(ClaudeCodeCliExecutableName);
    }

    private static IReadOnlyList<Process> GetRunningClaudeCodeProcesses()
    {
        var currentProcessIdentifier = Environment.ProcessId;
        var discoveredProcessIdentifiers = new HashSet<int>();
        var claudeCodeProcesses = new List<Process>();
        foreach (var claudeCodeProcessName in s_claudeCodeProcessNames)
        {
            foreach (var claudeCodeProcess in Process.GetProcessesByName(claudeCodeProcessName))
            {
                if (claudeCodeProcess.Id == currentProcessIdentifier || !IsClaudeCodeProcess(claudeCodeProcess) || !discoveredProcessIdentifiers.Add(claudeCodeProcess.Id))
                {
                    claudeCodeProcess.Dispose();
                    continue;
                }

                claudeCodeProcesses.Add(claudeCodeProcess);
            }
        }

        return claudeCodeProcesses;
    }

    private static bool IsClaudeCodeProcess(Process process)
    {
        var executableFilePath = TryGetProcessExecutableFilePath(process);
        if (executableFilePath.Contains("\\WindowsApps\\claude.ai-", StringComparison.OrdinalIgnoreCase)) return false;
        if (executableFilePath.Contains("\\AnthropicClaude\\", StringComparison.OrdinalIgnoreCase)) return true;
        if (executableFilePath.EndsWith("\\claude.exe", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(process.ProcessName, ClaudeCodeApplicationProcessName, StringComparison.Ordinal)
               || string.Equals(process.ProcessName, ClaudeCodeCliProcessName, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetRestartExecutableFilePaths(IReadOnlyList<Process> claudeCodeProcesses)
        => claudeCodeProcesses
            .Select(TryGetProcessExecutableFilePath)
            .Where(executableFilePath => !string.IsNullOrWhiteSpace(executableFilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void StopClaudeCodeProcesses(IReadOnlyList<Process> claudeCodeProcesses)
    {
        foreach (var claudeCodeProcess in claudeCodeProcesses)
        {
            try
            {
                if (claudeCodeProcess.HasExited) continue;
                claudeCodeProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }

        foreach (var claudeCodeProcess in claudeCodeProcesses)
        {
            try
            {
                if (!claudeCodeProcess.HasExited) claudeCodeProcess.WaitForExit(ProcessExitTimeoutMilliseconds);
            }
            catch { }
            finally { claudeCodeProcess.Dispose(); }
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

    private static bool TryStartProtocolAddress(string protocolAddress)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = protocolAddress,
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
}
