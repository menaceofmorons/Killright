using Killright.Integration.zKill;

namespace Killright.Storage.zKill;

public interface IzKillActivityCache
{
    Task<zKillActivity?> GetAsync(long characterId, TimeSpan maximumAge, CancellationToken cancellationToken = default);
    Task UpsertAsync(zKillActivity activity, CancellationToken cancellationToken = default);
}