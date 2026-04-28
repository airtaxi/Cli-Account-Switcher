using System.Text;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class FileLogService
{
    private const int MaximumLogEntryCount = 1000;
    private const string LogFileName = "codex-account-switch-logs.txt";

    private readonly Lock _logEntriesLock = new();
    private readonly List<string> _logEntries = [];
    private readonly string _logFilePath = Path.Combine(Constants.UserDataDirectory, LogFileName);

    public FileLogService() => ResetSessionLog();

    public bool HasLogs()
    {
        lock (_logEntriesLock)
            return _logEntries.Count > 0;
    }

    public async Task ExportAsync(string destinationFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        if (!string.Equals(Path.GetExtension(destinationFilePath), ".txt", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Log exports must use the .txt extension.", nameof(destinationFilePath));

        string logSnapshot;
        lock (_logEntriesLock)
        {
            if (_logEntries.Count == 0) throw new InvalidOperationException("There are no logs to export.");

            logSnapshot = CreateLogSnapshotCore();
        }

        await File.WriteAllTextAsync(destinationFilePath, logSnapshot, Encoding.UTF8);
    }

    public void WriteInformation(string source, string message) => WriteEntry("INFORMATION", source, message);

    public void WriteWarning(string source, string message) => WriteEntry("WARNING", source, message);

    public void WriteWarning(string source, string message, Exception exception) => WriteEntry("WARNING", source, AppendException(message, exception));

    public void WriteError(string source, string message) => WriteEntry("ERROR", source, message);

    public void WriteError(string source, string message, Exception exception) => WriteEntry("ERROR", source, AppendException(message, exception));

    private static string AppendException(string message, Exception exception)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine(message);
        AppendExceptionDetails(stringBuilder, exception);
        return stringBuilder.ToString().TrimEnd();
    }

    private static void AppendExceptionDetails(StringBuilder stringBuilder, Exception exception)
    {
        var currentException = exception;
        var exceptionDepth = 0;
        while (currentException is not null)
        {
            if (exceptionDepth > 0) stringBuilder.AppendLine("--->");

            stringBuilder.Append(currentException.GetType().FullName);
            stringBuilder.Append(": ");
            stringBuilder.AppendLine(currentException.Message);
            if (!string.IsNullOrWhiteSpace(currentException.StackTrace)) stringBuilder.AppendLine(currentException.StackTrace);

            currentException = currentException.InnerException;
            exceptionDepth++;
        }
    }

    private string CreateLogSnapshotCore() => string.Join($"{Environment.NewLine}{Environment.NewLine}", _logEntries);

    private void ResetSessionLog()
    {
        lock (_logEntriesLock)
        {
            _logEntries.Clear();
            TryWriteLogSnapshotToFileCore();
        }
    }

    private void TryWriteLogSnapshotToFileCore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            File.WriteAllText(_logFilePath, CreateLogSnapshotCore(), Encoding.UTF8);
        }
        catch { }
    }

    private void WriteEntry(string logLevel, string source, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{logLevel}] [{source}] {message.TrimEnd()}";

        lock (_logEntriesLock)
        {
            _logEntries.Add(logEntry);
            if (_logEntries.Count > MaximumLogEntryCount) _logEntries.RemoveRange(0, _logEntries.Count - MaximumLogEntryCount);

            TryWriteLogSnapshotToFileCore();
        }
    }
}
