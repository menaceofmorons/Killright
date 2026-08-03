namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryEvidenceImportRow(
    long KillmailId,
    DateTime KillmailTimeUtc,
    DateOnly EvidenceDateUtc,
    long? SolarSystemId,
    int ParticipantCount);