namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterOrphanedStagingFileCleaner
{
    /// <summary>
    /// Deletes the abandoned staging file left behind by a killed build-staging run,
    /// if and only if exactly one KillRight.History.*.duckdb file in the staging
    /// directory does not match the current live status's ActiveDatabaseFile. Returns
    /// the deleted file's full path, or null if zero or more than one candidate was
    /// found, in which case nothing is deleted.
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

        if (candidates.Length != 1)
            return null;

        File.Delete(candidates[0]);
        return candidates[0];
    }
}
