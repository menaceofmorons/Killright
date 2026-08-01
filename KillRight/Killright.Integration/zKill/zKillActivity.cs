namespace Killright.Integration.zKill;

public sealed record zKillActivity(
    long CharacterId,
    bool HasPublicActivityData,
    int? KillsWeek,
    int? SoloWeek,
    DateTimeOffset? LastActiveUtc,
    zKillActivityType? LastActivityType,
    DateTimeOffset CheckedAtUtc,
    string? Error = null);