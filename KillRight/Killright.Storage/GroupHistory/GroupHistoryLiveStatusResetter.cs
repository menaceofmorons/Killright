using System.Diagnostics;
using System.Text.Json;

namespace Killright.Storage.GroupHistory;

public static class GroupHistoryLiveStatusResetter
{
    public static void ResetUpdateInProgress(string? liveStatusPath = null)
    {
        var path = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();

        if (!File.Exists(path))
            return;

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

        if (!status.UpdateInProgress)
            return;

        var document = new
        {
            groupHistory = new
            {
                activeDatabaseFile = status.ActiveDatabaseFile,
                schemaVersion = status.SchemaVersion,
                lastCompletedDayUtc = status.LastCompletedDayUtc,
                lastUpdatedUtc = status.LastUpdatedUtc,
                updateInProgress = false,
                updateInProgressPid = (int?)null
            }
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Clears a stuck updateInProgress flag if the PID that set it is no
    /// longer running -- mirroring SingleInstanceLock's own stale-PID
    /// handling in lock.rs (Step CC-08.08.26.01). A no-op when
    /// updateInProgress is already false, when the recorded PID is still
    /// alive (a genuinely active run must never be clobbered), or when no
    /// PID was recorded at all -- a live status file written before this
    /// guide -- since there is then no way to verify staleness and the safe
    /// default is to leave it alone. Safe to call on every refresh.
    /// </summary>
    public static void ResetIfStale(string? liveStatusPath = null)
    {
        var path = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();
        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

        if (!status.UpdateInProgress)
            return;

        if (status.UpdateInProgressPid is int pid && IsProcessRunning(pid))
            return;

        if (status.UpdateInProgressPid is null)
            return;

        ResetUpdateInProgress(path);
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
