namespace PilotIntel.Integration.zKill;

internal sealed record zKillEndpointResult(
    bool HasPublicActivityData,
    int Count,
    DateTimeOffset? LastActivityUtc);