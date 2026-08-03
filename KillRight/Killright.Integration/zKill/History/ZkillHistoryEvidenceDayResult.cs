namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryEvidenceDayResult(
    ZkillHistoryDayResult DayResult,
    IReadOnlyList<ZkillHistoryEvidenceRecord> EvidenceRows,
    IReadOnlyList<ZkillHistoryParticipantRecord> ParticipantRows,
    ZkillHistoryExtractionTiming Timing);