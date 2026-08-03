namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryParticipantImportRow(
    long EvidenceId,
    long CharacterId,
    long? CorporationId,
    long? AllianceId,
    long? ShipTypeId);