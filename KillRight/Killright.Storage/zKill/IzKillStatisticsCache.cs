using Killright.Integration.zKill;

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
        CancellationToken cancellationToken = default);
}