using DuckDB.NET.Data;

namespace Killright.Storage.GroupHistory;

/// <summary>
/// Step 19.00.59: the outcome of <see cref="GroupHistoryActiveDatabasePathResolver.Repoint"/>.
/// Replaces the previous plain <c>bool</c> return so a caller can
/// distinguish "there was nothing to point at" from "there was something,
/// but it would not open" -- the two cases the application-side fallback
/// (Design Specification v5.4 Section 6.9.4) and Failed-folder routing must
/// treat differently.
/// </summary>
public enum GroupHistoryRepointResult
{
    /// <summary>The candidate path was null/blank, or no file exists there.</summary>
    CandidateFileMissing,

    /// <summary>
    /// The candidate file exists but failed to open as a DuckDB database.
    /// The active database path is left unchanged -- Design Specification
    /// v5.4 Section 6.9.4: "the application falls back to reopening the
    /// previous one rather than being left without an active history
    /// database."
    /// </summary>
    CandidateFileFailedToOpen,

    /// <summary>The candidate file exists, opened cleanly, and is now the active database path.</summary>
    Applied
}

public sealed class GroupHistoryActiveDatabasePathResolver
{
    private readonly object _lock = new();
    private string _currentDatabasePath;

    public GroupHistoryActiveDatabasePathResolver(string? defaultDatabasePath = null)
    {
        _currentDatabasePath = defaultDatabasePath ?? GroupHistoryDatabasePaths.GetDefaultDatabasePath();
    }

    public string GetCurrentDatabasePath()
    {
        lock (_lock)
        {
            return _currentDatabasePath;
        }
    }

    /// <summary>
    /// Step 19.00.59: repoints the active database path to
    /// <paramref name="candidateDatabaseFile"/> only after proving it opens
    /// cleanly as a DuckDB database -- opening, then immediately disposing,
    /// a connection to it (the same "Data Source={path}" connection-string
    /// convention Killright.Storage.Database.KillRightDatabase already
    /// uses), so no handle on the candidate lingers past this call either
    /// way. This is the application-side half of promotion Design
    /// Specification v5.4 Section 6.9.4 describes: "the application
    /// releases its handle on the currently active history database, opens
    /// the newly flagged one ... If opening the newly flagged database
    /// fails for any reason, the application falls back to reopening the
    /// previous one." As of this guide, no application code holds an open
    /// connection to the historic database at all -- there is no real
    /// consumer yet (Section 6.9.5: "Future consumer: the not-yet-designed
    /// Historic Analysis and Visualisation work") -- so "releases its
    /// handle" has no observable effect today. What this method proves,
    /// with real effect, is that the candidate is safe to adopt before the
    /// pointer moves, and that the previous path is left untouched (the
    /// fallback) when it is not.
    /// </summary>
    public GroupHistoryRepointResult Repoint(string? candidateDatabaseFile)
    {
        if (string.IsNullOrWhiteSpace(candidateDatabaseFile) || !File.Exists(candidateDatabaseFile))
            return GroupHistoryRepointResult.CandidateFileMissing;

        if (!OpensCleanly(candidateDatabaseFile))
            return GroupHistoryRepointResult.CandidateFileFailedToOpen;

        lock (_lock)
        {
            _currentDatabasePath = candidateDatabaseFile;
        }

        return GroupHistoryRepointResult.Applied;
    }

    private static bool OpensCleanly(string databasePath)
    {
        try
        {
            using var connection = new DuckDBConnection($"Data Source={databasePath}");
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
