namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistorySummaryBuildResult(
    DateOnly ImportDateUtc,
    long EvidenceRows,
    long ParticipantRows,
    long PairOccurrenceRows,
    long TotalSummaryRows,
    GroupHistoryPersistenceTiming Timing);