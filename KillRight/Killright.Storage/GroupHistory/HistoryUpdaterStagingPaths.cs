namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterStagingPaths
{
    public const string StagingFileSearchPattern = "KillRight.History.*.duckdb";

    // Matches killright_history_updater's staging.rs::PROGRESS_LOG_FILE_SUFFIX
    // paired with StagingFileSearchPattern's base naming (KillRight.History.*).
    public const string ProgressLogSearchPattern = "KillRight.History.*.progress.log";

    // Step 19.00.54: broader than StagingFileSearchPattern -- catches every
    // .duckdb file in the staging directory, not just ones matching the
    // versioned KillRight.History.*.duckdb naming introduced at 19.00.47.
    // Specifically needed for KillRight.HistoryUpdater.duckdb, the separate
    // fixed-name database the standalone create-schema/import-day/rebuild-summary
    // CLI commands have used since 19.00.43 (database_path.rs::get_default_database_path)
    // and which HistoryUpdaterWiper.WipeAll never deleted before this guide -- the
    // exact file the 19.00.53 tracker entry reports needed a manual delete after
    // a stale WAL caused an INSERT OR REPLACE failure. Used only by HistoryUpdaterWiper;
    // HistoryUpdaterOrphanedStagingFileCleaner keeps using StagingFileSearchPattern,
    // since its "exactly one candidate" reasoning is specific to the versioned
    // staging naming scheme, not the legacy fixed-name file.
    public const string AllDatabaseFileSearchPattern = "*.duckdb";

    // Step 19.00.54: catches any leftover DuckDB write-ahead-log file in the
    // staging directory (for example <database>.duckdb.wal after a process is
    // killed mid-transaction) -- the direct cause of the stale-file issue
    // recorded against 19.00.53 in Implementation-Guides-Tracker.xlsx.
    public const string AllWriteAheadLogSearchPattern = "*.wal";

    public static string GetStagingDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "HistoryUpdater");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
