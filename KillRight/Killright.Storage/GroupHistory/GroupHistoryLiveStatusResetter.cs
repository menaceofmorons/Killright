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
                updateInProgress = false
            }
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
