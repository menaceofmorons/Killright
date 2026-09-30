using Killright.Storage.Killmails;

namespace Killright.Storage.zKill;

public interface IPilotLastKillmailCache
{
    Task<PilotLastKillmailRecord?> GetAsync(long characterId, CancellationToken cancellationToken = default);

    Task UpsertAsync(PilotLastKillmailRecord record, CancellationToken cancellationToken = default);
}

public sealed record PilotLastKillmailRecord(
    long CharacterId,
    bool HasKillmail,
    long? KillmailId,
    PilotRecentKillmail? Killmail,
    DateTimeOffset CheckedAtUtc);
