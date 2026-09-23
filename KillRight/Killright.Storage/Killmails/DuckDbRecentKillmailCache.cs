using DuckDB.NET.Data;
using Killright.Integration.zKill;
using Killright.Shared.Data;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public sealed class DuckDbRecentKillmailCache : IRecentKillmailCache
{
    private readonly KillRightDatabase _database;
    private readonly int _recentWindowDays;

    public DuckDbRecentKillmailCache(KillRightDatabase database, int recentWindowDays)
    {
        _database = database;
        _recentWindowDays = recentWindowDays;
    }

    public Task<zKillActivity> GetDerivedActivityAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = ApplicationClock.UtcNow.AddDays(-7);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT kill_time_utc,
                                     TRUE AS is_loss,
                                     is_solo
                              FROM main.zkill_killmails
                              WHERE victim_character_id = {characterId}
                              UNION ALL
                              SELECT k.kill_time_utc,
                                     FALSE AS is_loss,
                                     k.is_solo
                              FROM main.zkill_killmail_attackers a
                              JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                              WHERE a.character_id = {characterId}
                              ORDER BY kill_time_utc DESC;
                              """;

        using var reader = command.ExecuteReader();
        var hasPublicActivityData = false;
        var killsWeek = 0;
        var soloWeek = 0;
        DateTimeOffset? lastActiveUtc = null;
        zKillActivityType? lastActivityType = null;

        while (reader.Read())
        {
            var killTimeUtc = reader.GetDateTimeOffset(0);
            var isLoss = reader.GetBoolean(1);
            var isSolo = reader.GetBoolean(2);

            if (killTimeUtc < cutoffUtc)
                continue;

            hasPublicActivityData = true;

            if (lastActiveUtc is null)
            {
                lastActiveUtc = killTimeUtc;
                lastActivityType = isLoss ? zKillActivityType.Loss : zKillActivityType.Kill;
            }

            if (!isLoss)
            {
                killsWeek++;

                if (isSolo)
                    soloWeek++;
            }
        }

        return Task.FromResult(new zKillActivity(
            characterId,
            hasPublicActivityData,
            hasPublicActivityData ? killsWeek : null,
            hasPublicActivityData ? soloWeek : null,
            lastActiveUtc,
            lastActivityType,
            ApplicationClock.UtcNow));
    }

    public Task RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = ApplicationClock.UtcNow.AddDays(-_recentWindowDays);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        using (var deleteAttackers = connection.CreateCommand())
        {
            deleteAttackers.Transaction = transaction;
            deleteAttackers.CommandText = $"""
                                DELETE FROM main.zkill_killmail_attackers
                                WHERE killmail_id IN (
                                    SELECT killmail_id
                                    FROM main.zkill_killmails
                                    WHERE is_qualifying = FALSE
                                      AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)}
                                );
                                """;
            deleteAttackers.ExecuteNonQuery();
        }

        using (var deleteKillmails = connection.CreateCommand())
        {
            deleteKillmails.Transaction = transaction;
            deleteKillmails.CommandText = $"""
                                DELETE FROM main.zkill_killmails
                                WHERE is_qualifying = FALSE
                                  AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)};
                                """;
            deleteKillmails.ExecuteNonQuery();
        }

        transaction.Commit();

        return Task.CompletedTask;
    }
}
