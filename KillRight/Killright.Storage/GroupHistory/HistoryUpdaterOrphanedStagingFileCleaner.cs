namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterOrphanedStagingFileCleaner
{
    /// <summary>
    /// Deletes every staging file in the given directory that is not the
    /// current ActiveDatabaseFile -- not just when there is exactly one, as
    /// before (Step CC-08.08.26.01). A killed run can leave more than one
    /// behind over time (for example, an earlier killed run's file that was
    /// never cleaned up, sitting alongside the one a later Stop just
    /// interrupted), and the previous "exactly one" check silently left all
    /// of them in place whenever that happened, with nothing reported.
    /// Returns the path of the most recently written one deleted -- the one
    /// the current Stop actually just interrupted -- or null if there was
    /// nothing to delete. Any other deleted candidate's paired progress log
    /// (Step CC-07.08.26.01) is deleted too, since it is stale cruft from an
    /// unrelated, already-reported incident; the most recent one's progress
    /// log is deliberately left in place, exactly as before, for the caller
    /// to report.
    /// </summary>
    public static string? TryCleanUpOrphan(string? stagingDirectory = null, string? liveStatusPath = null)
    {
        var directory = stagingDirectory ?? HistoryUpdaterStagingPaths.GetStagingDirectory();

        if (!Directory.Exists(directory))
            return null;

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(liveStatusPath);
        var activeDatabaseFile = status.ActiveDatabaseFile;
        var activeFullPath = string.IsNullOrEmpty(activeDatabaseFile) ? null : Path.GetFullPath(activeDatabaseFile);

        var candidates = Directory
            .GetFiles(directory, HistoryUpdaterStagingPaths.StagingFileSearchPattern)
            .Where(path => activeFullPath is null
                || !string.Equals(Path.GetFullPath(path), activeFullPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
            return null;

        var mostRecent = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();

        foreach (var path in candidates)
        {
            File.Delete(path);

            if (path != mostRecent)
            {
                var staleProgressLog = Path.ChangeExtension(path, ".progress.log");

                if (File.Exists(staleProgressLog))
                    File.Delete(staleProgressLog);
            }
        }

        return mostRecent;
    }
}
