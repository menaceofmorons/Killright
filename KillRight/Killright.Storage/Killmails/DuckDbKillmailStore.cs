using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Storage.Database;
using Killright.Storage.Scan;

namespace Killright.Storage.Killmails;

public sealed class DuckDbKillmailStore : IKillmailStore
{
    private readonly KillRightDatabase _database;
    private readonly int _qualificationFleetThreshold;

    public DuckDbKillmailStore(KillRightDatabase database, int qualificationFleetThreshold)
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

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        KillmailBulkWriter.Write(
            connection,
            transaction,
            [new PendingKillmails(0, characterId, killmails)],
            _qualificationFleetThreshold,
            ApplicationClock.UtcNow);

        transaction.Commit();

        return Task.CompletedTask;
    }
}
