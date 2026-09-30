using Killright.Integration.zKill;
using Killright.Storage.Database;

namespace Killright.Storage.zKill;

public interface IzKillActivityCache
{
    Task<zKillActivity?> GetAsync(long characterId, CancellationToken cancellationToken = default);
    Task UpsertAsync(zKillActivity activity, CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<long, zKillActivity>> GetManyAsync(
        IReadOnlyCollection<long> characterIds,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, zKillActivity>();

        foreach (var characterId in characterIds)
        {
            var activity = await GetAsync(characterId, cancellationToken);

            if (activity is not null)
                results[characterId] = activity;
        }

        return results;
    }
}
