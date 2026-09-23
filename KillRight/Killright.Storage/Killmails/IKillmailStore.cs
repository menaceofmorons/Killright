using Killright.Shared.Killmails;

namespace Killright.Storage.Killmails;

public interface IKillmailStore
{
    Task UpsertAsync(
        long characterId,
        IReadOnlyList<RawKillmail> killmails,
        CancellationToken cancellationToken = default);
}
