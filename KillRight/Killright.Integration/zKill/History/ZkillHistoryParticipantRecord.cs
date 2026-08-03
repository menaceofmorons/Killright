namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryParticipantRecord(
    long EvidenceId,
    long CharacterId,
    long? CorporationId,
    long? AllianceId,
    long? ShipTypeId);