using PilotIntel.Integration.zKill;

namespace PilotIntel.Storage.zKill;

public interface IzKillActivityCache
{
    Task<zKillActivity?> GetAsync(long characterId, TimeSpan maximumAge, CancellationToken cancellationToken = default);
    Task UpsertAsync(zKillActivity activity, CancellationToken cancellationToken = default);
}