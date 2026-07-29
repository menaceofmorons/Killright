using PilotIntel.Shared.zKill;
using PilotIntel.Shared.Killmails;

namespace PilotIntel.Integration.zKill;

public interface IzKillClient
{
    Task<IReadOnlyList<KillmailRecord>> GetRecentKillmailsAsync(
        long characterId,
        int pastSeconds,
        CancellationToken cancellationToken = default);

    Task<zKillStatistics?> GetStatisticsAsync(
        long characterId,
        CancellationToken cancellationToken = default);
}