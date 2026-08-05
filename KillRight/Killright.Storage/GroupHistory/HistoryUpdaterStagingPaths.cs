namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterStagingPaths
{
    public const string StagingFileSearchPattern = "KillRight.History.*.duckdb";

    public static string GetStagingDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "HistoryUpdater");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
