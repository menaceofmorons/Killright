using Killright.Integration.zKill;
using Killright.Shared.zKill;

namespace Killright.Storage.Killmails;

public interface IRecentKillmailCache
{
    Task<zKillActivity> GetDerivedActivityAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task<PilotRecentKillmail?> GetMostRecentKillmailAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task RemoveExpiredAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PilotRecentKillmail(
    DateTimeOffset KillTimeUtc,
    zKillActivityType ActivityType,
    long SystemId,
    long? ShipTypeId,
    long? VictimShipTypeId,
    int? AttackerCount);
