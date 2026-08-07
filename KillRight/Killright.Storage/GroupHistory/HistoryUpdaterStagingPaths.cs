namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterStagingPaths
{
    public const string StagingFileSearchPattern = "KillRight.History.*.duckdb";

    // Matches killright_history_updater's staging.rs::PROGRESS_LOG_FILE_SUFFIX
    // paired with StagingFileSearchPattern's base naming (KillRight.History.*).
    public const string ProgressLogSearchPattern = "KillRight.History.*.progress.log";

    public static string GetStagingDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "HistoryUpdater");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
