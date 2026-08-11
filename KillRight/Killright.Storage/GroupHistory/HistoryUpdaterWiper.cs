namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterWiper
{
    // Matches killright_history_updater's staging.rs::LATEST_VALIDATED_BUILD_MARKER_FILE_NAME.
    public const string LatestValidatedBuildMarkerFileName = "latest_validated_build.txt";

    /// <summary>
    /// Step 19.00.55: every directory WipeAll/FindRemainingArtifacts sweeps --
    /// the historic database root itself (catches the legacy fixed-name
    /// KillRight.HistoryUpdater.duckdb, which lives directly in the root, not
    /// a subfolder) plus its four Working/Live/Archive/Failed subfolders.
    /// Recomputed on every call rather than cached, since each directory is
    /// only ever a plain path join -- none of these need to exist for this
    /// list to be built; existence is checked per-directory by
    /// WipeDatabaseAndLogFiles/FindDatabaseAndLogFiles below.
    /// </summary>
    private static IReadOnlyList<string> AllSweptDirectories(string? rootDirectoryOverride)
    {
        var root = rootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();

        return new[]
        {
            root,
            Path.Combine(root, HistoryUpdaterStagingPaths.WorkingDirectoryName),
            Path.Combine(root, HistoryUpdaterStagingPaths.LiveDirectoryName),
            Path.Combine(root, HistoryUpdaterStagingPaths.ArchiveDirectoryName),
            Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName),
        };
    }

    /// <summary>
    /// Deletes every database file, write-ahead-log file, and progress log in
    /// a single directory that exists, without creating it if it doesn't.
    /// Shared by WipeAll and FindRemainingArtifacts (via
    /// FindDatabaseAndLogFiles) so the two stay in lockstep on exactly what
    /// "an artifact" means.
    /// </summary>
    private static int WipeDatabaseAndLogFiles(string directory)
    {
        var deletedCount = 0;

        if (!Directory.Exists(directory))
            return deletedCount;

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllDatabaseFileSearchPattern))
        {
            File.Delete(path);
            deletedCount++;
        }

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllWriteAheadLogSearchPattern))
        {
            File.Delete(path);
            deletedCount++;
        }

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllProgressLogSearchPattern))
        {
            File.Delete(path);
            deletedCount++;
        }

        return deletedCount;
    }

    private static IEnumerable<string> FindDatabaseAndLogFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllDatabaseFileSearchPattern))
            yield return path;

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllWriteAheadLogSearchPattern))
            yield return path;

        foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllProgressLogSearchPattern))
            yield return path;
    }

    /// <summary>
    /// Deletes every database file, write-ahead-log file, and progress log
    /// across the historic database root and all four Working/Live/Archive/
    /// Failed subfolders (Step 19.00.55 -- previously only the single flat
    /// staging directory), the latest-validated-build marker (Step 19.00.55:
    /// now read from the Working subfolder, matching
    /// killright_history_updater's staging.rs, which writes it alongside the
    /// working database), and the live status/swap-signal files. Returns the
    /// number of files deleted. Callers are responsible for confirming no
    /// build is in progress first.
    /// </summary>
    public static int WipeAll(string? rootDirectoryOverride = null, string? liveStatusPath = null, string? swapSignalPath = null)
    {
        var deletedCount = 0;

        foreach (var directory in AllSweptDirectories(rootDirectoryOverride))
        {
            deletedCount += WipeDatabaseAndLogFiles(directory);
        }

        var root = rootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var markerPath = Path.Combine(root, HistoryUpdaterStagingPaths.WorkingDirectoryName, LatestValidatedBuildMarkerFileName);

        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
            deletedCount++;
        }

        var statusPath = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();

        if (File.Exists(statusPath))
        {
            File.Delete(statusPath);
            deletedCount++;
        }

        var signalPath = swapSignalPath ?? GroupHistoryLiveConfigPaths.GetSwapSignalPath();

        if (File.Exists(signalPath))
        {
            File.Delete(signalPath);
            deletedCount++;
        }

        return deletedCount;
    }

    /// <summary>
    /// Step 19.00.54/19.00.55: lists every artifact WipeAll is responsible
    /// for removing that is still present, across the historic database root
    /// and all four subfolders, plus the Working-subfolder marker and the
    /// live status/swap-signal files. An empty result confirms a genuine
    /// clean slate. Safe to call at any time, not just immediately after
    /// WipeAll.
    /// </summary>
    public static IReadOnlyList<string> FindRemainingArtifacts(string? rootDirectoryOverride = null, string? liveStatusPath = null, string? swapSignalPath = null)
    {
        var remaining = new List<string>();

        foreach (var directory in AllSweptDirectories(rootDirectoryOverride))
        {
            remaining.AddRange(FindDatabaseAndLogFiles(directory));
        }

        var root = rootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var markerPath = Path.Combine(root, HistoryUpdaterStagingPaths.WorkingDirectoryName, LatestValidatedBuildMarkerFileName);

        if (File.Exists(markerPath))
            remaining.Add(markerPath);

        var statusPath = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();

        if (File.Exists(statusPath))
            remaining.Add(statusPath);

        var signalPath = swapSignalPath ?? GroupHistoryLiveConfigPaths.GetSwapSignalPath();

        if (File.Exists(signalPath))
            remaining.Add(signalPath);

        return remaining;
    }
}
