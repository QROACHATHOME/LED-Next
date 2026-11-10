namespace LedNext.Core;

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss.fff} [{Level}] {Message}";
}

/// <summary>Tiny thread-safe logger: optional file sink plus an event for the UI.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _filePath;

    public static event Action<LogEntry>? Logged;

    public static void SetFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        lock (Gate) _filePath = path;
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (Gate)
        {
            if (_filePath is not null)
            {
                try { File.AppendAllText(_filePath, entry + Environment.NewLine); }
                catch { /* logging must never take the app down */ }
            }
        }
        Logged?.Invoke(entry);
    }
}
