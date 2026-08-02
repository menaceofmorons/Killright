namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryImportDayStatus(
    DateOnly ImportDateUtc,
    string Status,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    int RawKillmailCount,
    int QualifyingKillmailCount,
    int ParticipantIndexRowCount,
    long PairOccurrenceCount,
    string? ErrorMessage);