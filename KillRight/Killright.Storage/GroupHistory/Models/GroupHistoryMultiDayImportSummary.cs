namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryMultiDayImportSummary(
    DateOnly StartDateUtc,
    DateOnly EndDateUtc,
    int TotalDays,
    int ImportedDays,
    int SkippedDays,
    int FailedDays,
    int RawKillmailCount,
    int QualifyingKillmailCount,
    int EvidenceRows,
    int ParticipantRows,
    long CandidatePairOccurrenceRows,
    long SummaryPairOccurrenceRows,
    long TotalSummaryRows,
    TimeSpan TotalRunElapsed,
    TimeSpan AverageImportedDayElapsed,
    IReadOnlyList<string> LogLines);