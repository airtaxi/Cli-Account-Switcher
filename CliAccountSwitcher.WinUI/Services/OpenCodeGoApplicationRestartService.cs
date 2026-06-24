using System.Diagnostics;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class OpenCodeGoApplicationRestartService
{
    private const string OpenCodeGoProcessName = "OpenCode";
    private const int ProcessExitTimeoutMilliseconds = 5000;

    public Task<bool> RestartOpenCodeGoAsync() => Task.Run(TryRestartOpenCodeGo);

    private static bool TryRestartOpenCodeGo()
    {
        try { return RestartOpenCodeGo(); }
        catch { return false; }
    }

    private static bool RestartOpenCodeGo()
    {
        var runningOpenCodeGoProcesses = GetRunningOpenCodeGoProcesses();
        if (runningOpenCodeGoProcesses.Count == 0) return TryStartOpenCodeGoFromDefaultPath();

        var executableFilePaths = GetRestartExecutableFilePaths(runningOpenCodeGoProcesses);

        StopOpenCodeGoProcesses(runningOpenCodeGoProcesses);

        return TryStartExecutableFilePaths(executableFilePaths);
    }

    private static IReadOnlyList<Process> GetRunningOpenCodeGoProcesses()
    {
        var currentProcessIdentifier = Environment.ProcessId;
        var discoveredProcessIdentifiers = new HashSet<int>();
        var openCodeGoProcesses = new List<Process>();
        foreach (var openCodeGoProcess in Process.GetProcessesByName(OpenCodeGoProcessName))
        {
            if (openCodeGoProcess.Id == currentProcessIdentifier || !IsOpenCodeGoProcess(openCodeGoProcess) || !discoveredProcessIdentifiers.Add(openCodeGoProcess.Id))
            {
                openCodeGoProcess.Dispose();
                continue;
            }

            openCodeGoProcesses.Add(openCodeGoProcess);
        }

        return openCodeGoProcesses;
    }

    private static bool IsOpenCodeGoProcess(Process process)
    {
        var executableFilePath = TryGetProcessExecutableFilePath(process);
        if (executableFilePath.EndsWith("\\OpenCodeGo.exe", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(process.ProcessName, OpenCodeGoProcessName, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetRestartExecutableFilePaths(IReadOnlyList<Process> openCodeGoProcesses) => openCodeGoProcesses.Select(TryGetProcessExecutableFilePath).Where(executableFilePath => !string.IsNullOrWhiteSpace(executableFilePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static void StopOpenCodeGoProcesses(IReadOnlyList<Process> openCodeGoProcesses)
    {
        foreach (var openCodeGoProcess in openCodeGoProcesses)
        {
            try
            {
                if (openCodeGoProcess.HasExited) continue;
                openCodeGoProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }

        foreach (var openCodeGoProcess in openCodeGoProcesses)
        {
            try
            {
                if (!openCodeGoProcess.HasExited)
                {
                    openCodeGoProcess.WaitForExit(ProcessExitTimeoutMilliseconds);
                }
            }
            catch { }
            finally { openCodeGoProcess.Dispose(); }
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
            if (TryStartExecutableFilePath(executableFilePath))
            {
                wasAnyExecutableFilePathStarted = true;
            }
        }

        if (!wasAnyExecutableFilePathStarted) return TryStartOpenCodeGoFromDefaultPath();
        return wasAnyExecutableFilePathStarted;
    }

    private static bool TryStartOpenCodeGoFromDefaultPath()
    {
        var defaultExecutableFilePath = Constants.OpenCodeGoExecutableFilePath;
        if (!File.Exists(defaultExecutableFilePath)) return false;
        return TryStartExecutableFilePath(defaultExecutableFilePath);
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
