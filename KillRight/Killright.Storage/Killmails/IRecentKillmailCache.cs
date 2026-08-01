using Killright.Integration.zKill;
using Killright.Shared.Killmails;

namespace Killright.Storage.Killmails;

public interface IRecentKillmailCache
{
    Task<IReadOnlyList<KillmailRecord>> GetForCharacterAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task<zKillActivity> GetDerivedActivityAsync(
        long characterId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        IReadOnlyList<KillmailRecord> killmails,
        CancellationToken cancellationToken = default);

    Task RemoveExpiredAsync(
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetMostRecentKillmailAsync(
        long characterId,
        CancellationToken cancellationToken = default);
}