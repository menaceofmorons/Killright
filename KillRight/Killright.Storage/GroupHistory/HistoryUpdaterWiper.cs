namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterWiper
{
    // Matches killright_history_updater's staging.rs::LATEST_VALIDATED_BUILD_MARKER_FILE_NAME.
    public const string LatestValidatedBuildMarkerFileName = "latest_validated_build.txt";

    /// <summary>
    /// Deletes every database file (Step 19.00.54: every *.duckdb file in the
    /// staging directory, not only ones matching the versioned
    /// KillRight.History.*.duckdb staging naming -- see
    /// HistoryUpdaterStagingPaths.AllDatabaseFileSearchPattern) and every
    /// leftover write-ahead-log file, the latest-validated-build marker, and
    /// the live status/swap-signal files. Returns the number of files deleted.
    /// Callers are responsible for confirming no build is in progress first.
    /// </summary>
    public static int WipeAll(string? stagingDirectory = null, string? liveStatusPath = null, string? swapSignalPath = null)
    {
        var directory = stagingDirectory ?? HistoryUpdaterStagingPaths.GetStagingDirectory();
        var deletedCount = 0;

        if (Directory.Exists(directory))
        {
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

            foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.ProgressLogSearchPattern))
            {
                File.Delete(path);
                deletedCount++;
            }

            var markerPath = Path.Combine(directory, LatestValidatedBuildMarkerFileName);

            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
                deletedCount++;
            }
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
    /// Step 19.00.54: the "confirmation check before proceeding to 19.00.55" the
    /// plan calls for -- lists every artifact WipeAll is responsible for removing
    /// that is still present. An empty result confirms a genuine clean slate: no
    /// working database (any *.duckdb file), no leftover write-ahead-log file, no
    /// orphaned progress log, no latest-validated-build marker, and no live config
    /// (groupHistory.status.json / database.new). Safe to call at any time, not
    /// just immediately after WipeAll -- for example to confirm state before a
    /// scale-gate run.
    /// </summary>
    public static IReadOnlyList<string> FindRemainingArtifacts(string? stagingDirectory = null, string? liveStatusPath = null, string? swapSignalPath = null)
    {
        var directory = stagingDirectory ?? HistoryUpdaterStagingPaths.GetStagingDirectory();
        var remaining = new List<string>();

        if (Directory.Exists(directory))
        {
            remaining.AddRange(Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllDatabaseFileSearchPattern));
            remaining.AddRange(Directory.GetFiles(directory, HistoryUpdaterStagingPaths.AllWriteAheadLogSearchPattern));
            remaining.AddRange(Directory.GetFiles(directory, HistoryUpdaterStagingPaths.ProgressLogSearchPattern));

            var markerPath = Path.Combine(directory, LatestValidatedBuildMarkerFileName);

            if (File.Exists(markerPath))
                remaining.Add(markerPath);
        }

        var statusPath = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();

        if (File.Exists(statusPath))
            remaining.Add(statusPath);

        var signalPath = swapSignalPath ?? GroupHistoryLiveConfigPaths.GetSwapSignalPath();

        if (File.Exists(signalPath))
            remaining.Add(signalPath);

        return remaining;
    }
}
