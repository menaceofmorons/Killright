namespace Killright.Storage.Diagnostics;

public static class EngineFailureLog
{
    public const long MaximumSizeBytes = 1 * 1024 * 1024;

    public static void Record(string message)
    {
        Record(EngineFailureLogPaths.GetDefaultLogPath(), message, DateTimeOffset.UtcNow);
    }

    public static void Record(string path, string message, DateTimeOffset utcNow)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaximumSizeBytes)
                File.Delete(path);

            File.AppendAllText(path, FormatLine(utcNow, message) + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the app or mask the original failure.
        }
    }

    public static string FormatLine(DateTimeOffset utcNow, string message)
    {
        return $"{utcNow:yyyy-MM-dd HH:mm:ss} UTC | {message}";
    }
}
