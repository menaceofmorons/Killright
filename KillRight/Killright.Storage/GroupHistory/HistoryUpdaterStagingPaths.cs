namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterStagingPaths
{
    // Step 19.00.55: subfolder names under the historic database root
    // (Design Specification v5.4 Section 6.9.2/4.7; the plan's 19.00.55
    // section). Working holds the in-progress import database this
    // application builds via killright_history_updater; Live, Archive, and
    // Failed are destinations for later steps (promotion copy at 19.00.56,
    // archive-on-swap at 19.00.60, failed-build handling at 19.00.58).
    public const string WorkingDirectoryName = "Working";
    public const string LiveDirectoryName = "Live";
    public const string ArchiveDirectoryName = "Archive";
    public const string FailedDirectoryName = "Failed";

    // Step 19.00.55: matches killright_history_updater's
    // staging.rs::allocate_new_working_filename, which now names the working
    // database CORE.KillRight.History.yymmdd.##.duckdb (replacing the
    // un-prefixed KillRight.History.*.duckdb naming used through 19.00.54).
    // Used by HistoryUpdaterOrphanedStagingFileCleaner, whose "exactly one
    // candidate" reasoning is specific to this versioned working naming
    // scheme, not the legacy fixed-name file below.
    public const string WorkingFileSearchPattern = "CORE.KillRight.History.*.duckdb";

    // Step 19.00.55: the promoted Live-folder naming 19.00.56 introduces --
    // the same yymmdd.## versioned scheme, without the CORE prefix. Nothing
    // produces a file matching this pattern yet as of this guide (19.00.56
    // is the first step that copies a file into Live); declared here now so
    // 19.00.56 has a single source of truth for the pattern rather than
    // inventing its own.
    public const string PromotedFileSearchPattern = "KillRight.History.*.duckdb";

    // Step 19.00.55: broadened from the previous
    // KillRight.History.*.progress.log to match a working-database progress
    // log regardless of the CORE prefix change above -- the same
    // naming-drift trap AllDatabaseFileSearchPattern was introduced to avoid
    // at 19.00.54.
    public const string AllProgressLogSearchPattern = "*.progress.log";

    // Step 19.00.54: broader than WorkingFileSearchPattern -- catches every
    // .duckdb file in a swept directory, not just ones matching a versioned
    // naming scheme. Specifically needed for KillRight.HistoryUpdater.duckdb,
    // the separate fixed-name database the standalone
    // create-schema/import-day/rebuild-summary CLI commands have used since
    // 19.00.43 (database_path.rs::get_default_database_path), which lives
    // directly under the historic database root, not a subfolder. Used only
    // by HistoryUpdaterWiper.
    public const string AllDatabaseFileSearchPattern = "*.duckdb";

    // Step 19.00.54: catches any leftover DuckDB write-ahead-log file (for
    // example <database>.duckdb.wal after a process is killed
    // mid-transaction).
    public const string AllWriteAheadLogSearchPattern = "*.wal";

    /// <summary>
    /// Step 19.00.55: the historic database root -- e.g.
    /// %LOCALAPPDATA%\KillRight\HistoryUpdater -- containing the Working,
    /// Live, Archive, and Failed subfolders. Renamed from
    /// GetStagingDirectory, which returned this same path back when it was
    /// still a single flat directory holding staging files directly. Does
    /// not create any subfolder itself; use
    /// GetWorkingDirectory/GetLiveDirectory/GetArchiveDirectory/
    /// GetFailedDirectory for that.
    /// </summary>
    public static string GetHistoryUpdaterRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "HistoryUpdater");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetWorkingDirectory()
    {
        var directory = Path.Combine(GetHistoryUpdaterRootDirectory(), WorkingDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetLiveDirectory()
    {
        var directory = Path.Combine(GetHistoryUpdaterRootDirectory(), LiveDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetArchiveDirectory()
    {
        var directory = Path.Combine(GetHistoryUpdaterRootDirectory(), ArchiveDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetFailedDirectory()
    {
        var directory = Path.Combine(GetHistoryUpdaterRootDirectory(), FailedDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
