namespace Killright.Storage.GroupHistory.Models;

public sealed class GroupHistoryLiveStatusDocument
{
    public GroupHistoryLiveStatus GroupHistory { get; init; } = new();
}

public sealed class GroupHistoryLiveStatus
{
    public string ActiveDatabaseFile { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public string? LastCompletedDayUtc { get; init; }

    public string? LastUpdatedUtc { get; init; }

    public bool UpdateInProgress { get; init; }

    public int? UpdateInProgressPid { get; init; }
}
