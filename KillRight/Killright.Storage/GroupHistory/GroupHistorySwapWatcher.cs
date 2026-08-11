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
                // Step 19.00.60 REV-B: only reached once Repoint has already
                // proven the new database opens cleanly (Applied is never
                // returned otherwise) -- Design Specification v5.4 Section
                // 6.9.4's required ordering: "only then moves the
                // previously active database to an archive location."
                // Deliberately does not use _resolver.GetCurrentDatabasePath()
                // -- see ArchiveSupersededLiveDatabases's own doc comment for
                // why that in-memory value cannot be trusted across
                // application restarts.
                ArchiveSupersededLiveDatabases(status.ActiveDatabaseFile);
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
    /// is guaranteed to exist at this point. Never touches Archive -- Failed
    /// and Archive are separate locations with separate retention policies
    /// (user-managed vs. single-generation, respectively; Design
    /// Specification v5.4 Section 6.9.4/6.9.7).
    /// </summary>
    private void MoveCandidateToFailed(string candidateDatabaseFile)
    {
        var root = _historyUpdaterRootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);
        Directory.CreateDirectory(failedDirectory);

        var destination = Path.Combine(failedDirectory, Path.GetFileName(candidateDatabaseFile));
        File.Move(candidateDatabaseFile, destination, overwrite: true);
    }

    /// <summary>
    /// Step 19.00.60 REV-B: moves whatever is superseded in Live into
    /// Archive, and enforces single-generation retention -- derived entirely
    /// from Live's own contents on disk, never from
    /// GroupHistoryActiveDatabasePathResolver.GetCurrentDatabasePath(). That
    /// in-memory value is reconstructed fresh by App.OnStartup on every
    /// application launch (defaulting to
    /// GroupHistoryDatabasePaths.GetDefaultDatabasePath() until Repoint
    /// first succeeds within that process's own lifetime), so it has no
    /// memory of a swap that happened during an earlier run of the
    /// application -- the realistic, normal case, since KillRight is not
    /// expected to stay running continuously between builds. Live itself,
    /// by contrast, is a real filesystem location that survives every
    /// restart, so it is the only durable source of truth for "what was
    /// superseded" available today (Design Specification v5.4 Section 6.9.4:
    /// "only then moves the previously active database to an archive
    /// location ... Exactly one previous database is retained in the
    /// archive location at a time; each successful swap deletes whatever
    /// was archived before it, so superseded databases do not accumulate").
    ///
    /// Lists every file in Live matching
    /// HistoryUpdaterStagingPaths.PromotedFileSearchPattern other than
    /// newlyActiveDatabasePath (the file Repoint just adopted). If none are
    /// found (the very first build on a machine, or a redundant
    /// CheckAndApply call), this is a no-op. If exactly one is found, it is
    /// archived. If more than one is found -- multiple builds landed in
    /// Live with no intervening application launch to archive them one at a
    /// time -- only the single most-recently-modified one is archived
    /// (single-generation retention has room for exactly one); anything
    /// older is deleted outright from Live, since there is no retention
    /// slot for it and no case for leaving it to accumulate silently
    /// either. Existing Archive contents are always deleted before the
    /// chosen file is moved in, never after, so the file just archived can
    /// never be the one this same call mistakenly deletes.
    /// </summary>
    private void ArchiveSupersededLiveDatabases(string newlyActiveDatabasePath)
    {
        var root = _historyUpdaterRootDirectoryOverride ?? HistoryUpdaterStagingPaths.GetHistoryUpdaterRootDirectory();
        var liveDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.LiveDirectoryName);

        if (!Directory.Exists(liveDirectory))
            return;

        var newlyActiveFullPath = Path.GetFullPath(newlyActiveDatabasePath);

        var supersededLiveFiles = Directory
            .GetFiles(liveDirectory, HistoryUpdaterStagingPaths.PromotedFileSearchPattern)
            .Where(path => !string.Equals(Path.GetFullPath(path), newlyActiveFullPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (supersededLiveFiles.Count == 0)
            return;

        var mostRecentlySuperseded = supersededLiveFiles[0];

        foreach (var staleOrphan in supersededLiveFiles.Skip(1))
            File.Delete(staleOrphan);

        var archiveDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
        Directory.CreateDirectory(archiveDirectory);

        foreach (var existingArchivedFile in Directory.GetFiles(archiveDirectory))
            File.Delete(existingArchivedFile);

        var destination = Path.Combine(archiveDirectory, Path.GetFileName(mostRecentlySuperseded));
        File.Move(mostRecentlySuperseded, destination, overwrite: true);
    }
}
