using Killright.Integration.zKill;
using Killright.Storage.Database;

namespace Killright.Storage.zKill;
using Killright.Shared.zKill;

public interface IzKillStatisticsCache
{
    Task<zKillStatistics?> GetAsync(
        long characterId,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        long characterId,
        zKillStatistics statistics,
        string generalStyle,
        bool noHistory,
        CancellationToken cancellationToken = default);

    Task ClearNoHistoryMarkerAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<long, zKillStatistics>> GetManyAsync(
        IReadOnlyCollection<long> characterIds,
        TimeSpan maximumAge,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, zKillStatistics>();

        foreach (var characterId in characterIds)
        {
            var statistics = await GetAsync(characterId, maximumAge, cancellationToken);

            if (statistics is not null)
                results[characterId] = statistics;
        }

        return results;
    }
}
