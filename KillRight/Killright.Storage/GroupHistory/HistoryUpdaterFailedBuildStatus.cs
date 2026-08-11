namespace Killright.Storage.GroupHistory;

/// <summary>
/// Step 19.00.58: read-only inspection of the Failed folder (Design
/// Specification v5.4 Section 6.9.4 -- "a copy that fails validation is
/// moved to a separate failed-build folder and the user is notified" --
/// and Section 6.9.7 Known Limitations -- "Failed builds are moved to a
/// dedicated folder and the user is notified, but nothing removes them
/// automatically; disk cleanup of failed builds is the user's
/// responsibility until cleanup/analysis tooling exists"). This class only
/// reports what killright_history_updater's promotion::move_to_failed
/// (Step 19.00.56) has already placed in the Failed folder -- it never
/// creates, deletes, or moves anything itself, and never force-creates the
/// Failed folder just to check it. Kept separate from
/// HistoryUpdaterStagingPaths/HistoryUpdaterWiper so
/// GroupDetectionHistoryPilotWindow's notification surface can depend on a
/// small, independently testable read-only surface rather than calling
/// Directory.GetFiles directly.
/// </summary>
public static class HistoryUpdaterFailedBuildStatus
{
    /// <summary>
    /// Lists the file names currently in the Failed folder that match the
    /// same promoted-database naming pattern
    /// killright_history_updater's promotion::move_to_failed produces
    /// (HistoryUpdaterStagingPaths.PromotedFileSearchPattern -- the file
    /// keeps its versioned Live-folder name when moved to Failed, per
    /// promotion.rs's move_to_failed doc comment), ordered newest-first by
    /// last-write time in UTC. Returns an empty list, without creating the
    /// Failed folder, when it does not exist or contains no matching files.
    ///
    /// <paramref name="rootDirectoryOverride"/> follows the same convention
    /// as HistoryUpdaterWiper's rootDirectoryOverride: it is the historic
    /// database root directly (e.g. %LOCALAPPDATA%\KillRight\HistoryUpdater),
    /// not a subfolder -- Failed is joined onto it here.
    /// </summary>
    public static IReadOnlyList<string> FindFailedBuildFileNames(string? rootDirectoryOverride = null)
    {
        var root = rootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);

        if (!Directory.Exists(failedDirectory))
            return Array.Empty<string>();

        return Directory
            .GetFiles(failedDirectory, HistoryUpdaterStagingPaths.PromotedFileSearchPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => Path.GetFileName(path)!)
            .ToList();
    }
}
