namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterWiper
{
    // Matches killright_history_updater's staging.rs::LATEST_VALIDATED_BUILD_MARKER_FILE_NAME.
    public const string LatestValidatedBuildMarkerFileName = "latest_validated_build.txt";

    /// <summary>
    /// Deletes every staging database file, the latest-validated-build marker, and
    /// the live status/swap-signal files. Returns the number of files deleted.
    /// Callers are responsible for confirming no build is in progress first.
    /// </summary>
    public static int WipeAll(string? stagingDirectory = null, string? liveStatusPath = null, string? swapSignalPath = null)
    {
        var directory = stagingDirectory ?? HistoryUpdaterStagingPaths.GetStagingDirectory();
        var deletedCount = 0;

        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.GetFiles(directory, HistoryUpdaterStagingPaths.StagingFileSearchPattern))
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
}
