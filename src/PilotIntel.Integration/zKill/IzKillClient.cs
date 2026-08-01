using PilotIntel.Shared.Killmails;
using PilotIntel.Shared.zKill;

namespace PilotIntel.Integration.zKill;

public interface IzKillClient
{
    Task<IReadOnlyList<KillmailRecord>> GetRecentKillmailsAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task<zKillStatistics?> GetStatisticsAsync(
        long characterId,
        CancellationToken cancellationToken = default);
}