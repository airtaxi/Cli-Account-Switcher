using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.Api.Providers.ClaudeCode.Infrastructure;

internal sealed class ClaudeCodeCliRunner
{
    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (await TryRunVersionCommandAsync("--version", cancellationToken)) return;
        if (await TryRunVersionCommandAsync("-v", cancellationToken)) return;
        throw new ProviderInstallNotFoundException("Claude Code CLI was not found. Ensure `claude` is installed and available on PATH.");
    }

    public async Task<ClaudeCodeProcessResult> RunInteractiveLoginAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(cancellationToken);

        var loginProcessStartInfo = new ProcessStartInfo
        {
            FileName = "claude",
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        loginProcessStartInfo.ArgumentList.Add("auth");
        loginProcessStartInfo.ArgumentList.Add("login");

        try
        {
            using var loginProcess = Process.Start(loginProcessStartInfo) ?? throw new ProviderInstallNotFoundException("Claude Code CLI could not be started.");
            await loginProcess.WaitForExitAsync(cancellationToken);
            if (loginProcess.ExitCode == 0) return new ClaudeCodeProcessResult { ExitCode = loginProcess.ExitCode };
        }
        catch (Win32Exception exception)
        {
            throw new ProviderInstallNotFoundException("Claude Code CLI was not found. Ensure `claude` is installed and available on PATH.", exception);
        }

        var fallbackResult = await TryRunSlashLoginFallbackAsync(cancellationToken);
        if (fallbackResult.ExitCode == 0 && LooksLikeLoginSucceeded(fallbackResult)) return fallbackResult;
        throw new ProviderActionRequiredException("claude auth login failed. Run /login in Claude Code manually, then run save-current-account.");
    }

    public async Task<ClaudeCodeProcessResult> RunCapturedAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var processStartInfo = CreateRedirectedProcessStartInfo(arguments);

        try
        {
            using var process = Process.Start(processStartInfo) ?? throw new ProviderInstallNotFoundException("Claude Code CLI could not be started.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new ClaudeCodeProcessResult
            {
                ExitCode = process.ExitCode,
                OutputText = await outputTask,
                ErrorText = await errorTask
            };
        }
        catch (Win32Exception exception)
        {
            throw new ProviderInstallNotFoundException("Claude Code CLI was not found. Ensure `claude` is installed and available on PATH.", exception);
        }
    }

    public async IAsyncEnumerable<string> StreamOutputLinesAsync(IReadOnlyList<string> arguments, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var processStartInfo = CreateRedirectedProcessStartInfo(arguments);

        using var process = StartProcess(processStartInfo);
        var errorTextTask = process.StandardError.ReadToEndAsync(cancellationToken);

        while (true)
        {
            var outputLine = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (outputLine is null) break;
            yield return outputLine;
        }

        await process.WaitForExitAsync(cancellationToken);
        var errorText = await errorTextTask;
        if (process.ExitCode != 0) throw new ProviderActionRequiredException($"Claude Code command failed with exit code {process.ExitCode}: {errorText}");
    }

    private static async Task<bool> TryRunVersionCommandAsync(string versionArgument, CancellationToken cancellationToken)
    {
        try
        {
            var processStartInfo = CreateRedirectedProcessStartInfo([versionArgument]);
            using var process = Process.Start(processStartInfo);
            if (process is null) return false;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<ClaudeCodeProcessResult> TryRunSlashLoginFallbackAsync(CancellationToken cancellationToken)
    {
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(60));

        var processStartInfo = CreateRedirectedProcessStartInfo([]);
        using var process = StartProcess(processStartInfo);
        await process.StandardInput.WriteLineAsync("/login");
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCancellationTokenSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutCancellationTokenSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); }
            catch { }
        }

        return new ClaudeCodeProcessResult
        {
            ExitCode = process.HasExited ? process.ExitCode : -1,
            OutputText = await CompleteTextReadAsync(outputTask),
            ErrorText = await CompleteTextReadAsync(errorTask)
        };
    }

    private static Process StartProcess(ProcessStartInfo processStartInfo)
    {
        try
        {
            return Process.Start(processStartInfo) ?? throw new ProviderInstallNotFoundException("Claude Code CLI could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new ProviderInstallNotFoundException("Claude Code CLI was not found. Ensure `claude` is installed and available on PATH.", exception);
        }
    }

    private static ProcessStartInfo CreateRedirectedProcessStartInfo(IReadOnlyList<string> arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "claude",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);
        return processStartInfo;
    }

    private static bool LooksLikeLoginSucceeded(ClaudeCodeProcessResult processResult)
    {
        var combinedText = $"{processResult.OutputText}\n{processResult.ErrorText}".ToLowerInvariant();
        return combinedText.Contains("login", StringComparison.Ordinal) || combinedText.Contains("logged in", StringComparison.Ordinal) || combinedText.Contains("success", StringComparison.Ordinal);
    }

    private static async Task<string> CompleteTextReadAsync(Task<string> textTask)
    {
        try { return await textTask; }
        catch { return ""; }
    }
}
