namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryPersistenceTiming(
    TimeSpan ConnectionOpenElapsed,
    TimeSpan TransactionBeginElapsed,
    TimeSpan EvidenceBatchPreparationElapsed,
    TimeSpan EvidenceBatchExecutionElapsed,
    TimeSpan EvidenceInsertElapsed,
    TimeSpan ParticipantBatchPreparationElapsed,
    TimeSpan ParticipantBatchExecutionElapsed,
    TimeSpan ParticipantInsertElapsed,
    TimeSpan SummaryPreCountElapsed,
    TimeSpan SummaryUpdateElapsed,
    TimeSpan SummaryPostCountElapsed,
    TimeSpan StatusUpdateElapsed,
    TimeSpan TransactionCommitElapsed,
    TimeSpan TotalPersistenceElapsed,
    TimeSpan UnaccountedPersistenceElapsed);