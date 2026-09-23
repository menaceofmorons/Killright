using Killright.Integration.zKill;

namespace Killright.Storage.Killmails;

public interface IRecentKillmailCache
{
    Task<zKillActivity> GetDerivedActivityAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task RemoveExpiredAsync(
        CancellationToken cancellationToken = default);
}
