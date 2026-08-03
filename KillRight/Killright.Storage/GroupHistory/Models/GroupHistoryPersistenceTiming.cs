namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryPersistenceTiming(
    TimeSpan EvidenceInsertElapsed,
    TimeSpan ParticipantInsertElapsed,
    TimeSpan SummaryUpdateElapsed,
    TimeSpan StatusUpdateElapsed,
    TimeSpan TransactionCommitElapsed,
    TimeSpan TotalPersistenceElapsed);