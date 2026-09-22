namespace Killright.Storage.Diagnostics;

public static class EngineFailureLogPaths
{
    public const string FileName = "killright-engine.log";

    public static string GetDefaultLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, FileName);
    }
}
