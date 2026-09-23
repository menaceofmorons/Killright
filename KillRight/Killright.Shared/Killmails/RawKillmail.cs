namespace Killright.Shared.Killmails;

public sealed record RawKillmail(
    long KillmailId,
    string? KillmailHash,
    DateTimeOffset KillTimeUtc,
    long SystemId,
    long? LocationId,
    long? VictimCharacterId,
    long? VictimShipTypeId,
    bool IsSolo,
    bool IsNpc,
    IReadOnlyList<KillmailAttacker> Attackers);

public sealed record KillmailAttacker(
    long? CharacterId,
    long? CorporationId,
    long? AllianceId,
    long? ShipTypeId);
