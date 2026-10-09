using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Storage.Database;
using Killright.Storage.Scan;

namespace Killright.Storage.Killmails;

public sealed class KillmailStore : IKillmailStore
{
    private readonly KillRightDatabase _database;
    private readonly int _qualificationFleetThreshold;

    public KillmailStore(KillRightDatabase database, int qualificationFleetThreshold)
    {
        _database = database;
        _qualificationFleetThreshold = qualificationFleetThreshold;
    }

    public Task UpsertAsync(
        long characterId,
        IReadOnlyList<RawKillmail> killmails,
        CancellationToken cancellationToken = default)
    {
        if (killmails.Count == 0)
            return Task.CompletedTask;

        using var scope = _database.BeginWrite();

        KillmailBulkWriter.Write(
            scope.Connection,
            scope.Transaction,
            [new PendingKillmails(0, characterId, killmails)],
            _qualificationFleetThreshold,
            ApplicationClock.UtcNow);

        scope.Commit();

        return Task.CompletedTask;
    }
}
