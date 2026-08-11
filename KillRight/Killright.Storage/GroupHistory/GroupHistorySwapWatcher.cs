namespace Killright.Storage.GroupHistory;

/// <summary>
/// Step 19.00.59: the outcome of <see cref="GroupHistorySwapWatcher.CheckAndApply"/>.
/// </summary>
public enum GroupHistorySwapResult
{
    /// <summary>No database.new sentinel was present; nothing to apply.</summary>
    NoSentinel,

    /// <summary>The sentinel was found and the active database path was repointed to the newly validated Live-folder copy.</summary>
    Applied,

    /// <summary>
    /// The sentinel was found, but groupHistory.status.json's
    /// activeDatabaseFile is blank or does not exist on disk. The previous
    /// active path is left in place.
    /// </summary>
    CandidateFileMissing,

    /// <summary>
    /// The sentinel was found and the candidate file exists, but it failed
    /// to open as a DuckDB database. The previous active path is left in
    /// place (Design Specification v5.4 Section 6.9.4: "the application
    /// falls back to reopening the previous one rather than being left
    /// without an active history database"), and the candidate is moved
    /// from Live to Failed, mirroring killright_history_updater's own
    /// promotion::move_to_failed for the equivalent Rust-side case.
    /// </summary>
    FailedToOpenFallenBackToPrevious
}

public sealed class GroupHistorySwapWatcher
{
    private readonly GroupHistoryActiveDatabasePathResolver _resolver;
    private readonly string _swapSignalPath;
    private readonly string? _liveStatusPath;
    private readonly string? _historyUpdaterRootDirectoryOverride;

    public GroupHistorySwapWatcher(
        GroupHistoryActiveDatabasePathResolver resolver,
        string? swapSignalPath = null,
        string? liveStatusPath = null,
        string? historyUpdaterRootDirectoryOverride = null)
    {
        _resolver = resolver;
        _swapSignalPath = swapSignalPath ?? GroupHistoryLiveConfigPaths.GetSwapSignalPath();
        _liveStatusPath = liveStatusPath;
        _historyUpdaterRootDirectoryOverride = historyUpdaterRootDirectoryOverride;
    }

    public GroupHistorySwapResult CheckAndApply()
    {
        if (!File.Exists(_swapSignalPath))
            return GroupHistorySwapResult.NoSentinel;

        File.Delete(_swapSignalPath);

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(_liveStatusPath);
        var repointResult = _resolver.Repoint(status.ActiveDatabaseFile);

        switch (repointResult)
        {
            case GroupHistoryRepointResult.Applied:
                return GroupHistorySwapResult.Applied;

            case GroupHistoryRepointResult.CandidateFileFailedToOpen:
                MoveCandidateToFailed(status.ActiveDatabaseFile);
                return GroupHistorySwapResult.FailedToOpenFallenBackToPrevious;

            default:
                return GroupHistorySwapResult.CandidateFileMissing;
        }
    }

    /// <summary>
    /// Moves a Live-folder copy that failed to open into Failed, retaining
    /// its versioned filename for diagnosis -- the C#-side counterpart to
    /// killright_history_updater's promotion::move_to_failed, for the case
    /// where the Rust-side validation gate already passed (the file is
    /// sitting in Live with its live flag set) but this application process
    /// still cannot open it. Landing here is picked up by Step 19.00.58's
    /// existing HistoryUpdaterFailedBuildStatus notification, which reads
    /// Failed by its actual contents, not by why a file arrived there. Only
    /// called when GroupHistoryActiveDatabasePathResolver.Repoint has
    /// already confirmed the candidate file exists (CandidateFileFailedToOpen
    /// is never returned for a missing candidate), so candidateDatabaseFile
    /// is guaranteed to exist at this point.
    /// </summary>
    private void MoveCandidateToFailed(string candidateDatabaseFile)
    {
        var root = _historyUpdaterRootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);
        Directory.CreateDirectory(failedDirectory);

        var destination = Path.Combine(failedDirectory, Path.GetFileName(candidateDatabaseFile));
        File.Move(candidateDatabaseFile, destination, overwrite: true);
    }
}
