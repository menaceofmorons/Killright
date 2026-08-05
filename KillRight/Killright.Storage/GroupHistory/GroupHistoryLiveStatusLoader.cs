using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Killright.Storage.GroupHistory.Models;

namespace Killright.Storage.GroupHistory;

public static class GroupHistoryLiveStatusLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static GroupHistoryLiveStatus LoadOrDefault(string? liveStatusPath = null)
    {
        var path = liveStatusPath ?? GroupHistoryLiveConfigPaths.GetLiveStatusPath();

        try
        {
            if (!File.Exists(path))
                return new GroupHistoryLiveStatus();

            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<GroupHistoryLiveStatusDocument>(json, JsonOptions);
            return document?.GroupHistory ?? new GroupHistoryLiveStatus();
        }
        catch
        {
            return new GroupHistoryLiveStatus();
        }
    }
}
