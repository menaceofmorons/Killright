namespace Killright.Shared.Killmails;

public sealed record KillmailRecord(
    long KillmailId,
    string? KillmailHash,
    long CharacterId,
    DateTimeOffset KillTimeUtc,
    bool IsLoss,
    int AttackerCount,
    bool IsSolo,
    long? ShipTypeId,
    long? SystemId,
    long? LocationId,
    bool IsNpc,
    DateTimeOffset CachedAtUtc);