namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryEvidenceRecord(
    long KillmailId,
    DateTime KillmailTimeUtc,
    DateOnly EvidenceDateUtc,
    long? SolarSystemId,
    long? VictimCharacterId,
    long? VictimCorporationId,
    long? VictimAllianceId,
    long? VictimShipTypeId,
    int ParticipantCount);