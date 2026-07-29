using PilotIntel.Integration.zKill;

namespace PilotIntel.Storage.zKill;
using PilotIntel.Shared.zKill;

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