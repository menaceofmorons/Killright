using Killright.Storage.GroupHistory;

namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryDatabaseStatus(
    bool DatabaseExists,
    bool SchemaExists,
    int? SchemaVersion,
    DateOnly? HistoryStartDayUtc,
    DateOnly? LastCompletedDayUtc,
    DateTime? CreatedUtc,
    DateTime? LastUpdateUtc,
    long? AverageImportMillisecondsPerDay,
    string? LastImportResult)
{
    public bool IsUsable => DatabaseExists && SchemaExists && SchemaVersion == GroupHistoryConstants.SchemaVersion;
}