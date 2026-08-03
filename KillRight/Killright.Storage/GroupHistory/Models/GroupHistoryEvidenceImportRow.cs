namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryEvidenceImportRow(
    long KillmailId,
    DateTime KillmailTimeUtc,
    DateOnly EvidenceDateUtc,
    long? SolarSystemId,
    long? VictimCharacterId,
    long? VictimCorporationId,
    long? VictimAllianceId,
    long? VictimShipTypeId,
    int ParticipantCount);