using Killright.Shared.zKill;

namespace Killright.Integration.zKill;

public interface IzKillClient
{
    Task<zKillRecentKillmailResult> GetRecentKillmailsAsync(
        long characterId,
        int pastSeconds,
        CancellationToken cancellationToken = default);

    Task<zKillStatisticsResult> GetStatisticsAsync(
        long characterId,
        CancellationToken cancellationToken = default);
}
