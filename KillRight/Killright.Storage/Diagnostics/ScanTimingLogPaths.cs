namespace Killright.Storage.Diagnostics;

public static class ScanTimingLogPaths
{
    public const string FilePrefix = "killright-timing-";

    private static readonly string SessionFileName =
        $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

    public static string GetDefaultLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, SessionFileName);
    }
}
