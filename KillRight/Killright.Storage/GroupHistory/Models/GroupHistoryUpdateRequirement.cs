namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryUpdateRequirement(
    bool DatabaseExists,
    bool SchemaExists,
    bool InitialCreationRequired,
    DateOnly? FirstMissingDayUtc,
    DateOnly? LastMissingDayUtc,
    int MissingDayCount,
    TimeSpan EstimatedDuration,
    bool PromptRequired,
    string Message)
{
    public bool UpdateRequired => InitialCreationRequired || MissingDayCount > 0;
}