namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryDayResult(
    DateOnly Date,
    string Url,
    bool Succeeded,
    int RawKillmailCount,
    int RawAttackerCount,
    int PodKillmailCount,
    int InsufficientAttackerKillmailCount,
    int FleetKillmailCount,
    int QualifyingKillmailCount,
    int QualifyingAttackerCount,
    long CandidatePairOccurrenceRows,
    int MaxQualifyingAttackersOnKillmail,
    string? ErrorMessage);