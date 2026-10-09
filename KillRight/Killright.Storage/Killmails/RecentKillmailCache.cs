using Microsoft.Data.Sqlite;
using Killright.Integration.zKill;
using Killright.Shared.Data;
using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public sealed class RecentKillmailCache : IRecentKillmailCache
{
    private static readonly string PodShipTypeIdList = KillmailQualification.PodShipTypeIdSqlList;

    private readonly KillRightDatabase _database;
    private readonly int _recentWindowDays;

    public RecentKillmailCache(KillRightDatabase database, int recentWindowDays)
    {
        _database = database;
        _recentWindowDays = recentWindowDays;
    }

    public Task<PilotRecentKillmail?> GetMostRecentKillmailAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT kill_time_utc,
                                     1 AS is_loss,
                                     system_id,
                                     victim_ship_type_id AS ship_type_id,
                                     NULL AS victim_ship_type_id,
                                     NULL AS attacker_count,
                                     NULL AS weapon_type_id
                              FROM main.zkill_killmails
                              WHERE victim_character_id = {characterId}
                                AND (victim_ship_type_id IS NULL OR victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                              UNION ALL
                              SELECT k.kill_time_utc,
                                     0 AS is_loss,
                                     k.system_id,
                                     a.ship_type_id AS ship_type_id,
                                     k.victim_ship_type_id,
                                     k.unique_attacker_count AS attacker_count,
                                     a.weapon_type_id
                              FROM main.zkill_killmail_attackers a
                              JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                              WHERE a.character_id = {characterId}
                                AND (k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                              ORDER BY kill_time_utc DESC
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<PilotRecentKillmail?>(null);

        var isLoss = reader.GetBoolean(1);

        return Task.FromResult<PilotRecentKillmail?>(new PilotRecentKillmail(
            reader.GetUtcDateTimeOffset(0),
            isLoss ? zKillActivityType.Loss : zKillActivityType.Kill,
            reader.GetInt64(2),
            reader.GetNullableInt64(3),
            reader.GetNullableInt64(4),
            reader.GetNullableInt32(5),
            reader.GetNullableInt64(6)));
    }

    public Task RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = ApplicationClock.UtcNow.AddDays(-_recentWindowDays);

        using var scope = _database.BeginWrite();

        using (var dropStaleProtectedFloor = scope.Connection.CreateCommand())
        {
            dropStaleProtectedFloor.Transaction = scope.Transaction;
            dropStaleProtectedFloor.CommandText = "DROP TABLE IF EXISTS temp.protected_killmail_ids;";
            dropStaleProtectedFloor.ExecuteNonQuery();
        }

        using (var buildProtectedFloor = scope.Connection.CreateCommand())
        {
            buildProtectedFloor.Transaction = scope.Transaction;
            buildProtectedFloor.CommandText = $"""
                                CREATE TEMP TABLE protected_killmail_ids AS
                                WITH scanned_pilots AS (
                                    SELECT character_id
                                    FROM main.zkill_activity_cache
                                ),
                                pilot_kill_times AS (
                                    SELECT victim_character_id AS character_id, kill_time_utc
                                    FROM main.zkill_killmails
                                    WHERE victim_character_id IN (SELECT character_id FROM scanned_pilots)
                                      AND (victim_ship_type_id IS NULL OR victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                                    UNION ALL
                                    SELECT a.character_id, k.kill_time_utc
                                    FROM main.zkill_killmail_attackers a
                                    JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                                    WHERE a.character_id IN (SELECT character_id FROM scanned_pilots)
                                      AND (k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                                ),
                                pilot_floor AS (
                                    SELECT character_id, MAX(kill_time_utc) AS floor_kill_time_utc
                                    FROM pilot_kill_times
                                    GROUP BY character_id
                                )
                                SELECT k.killmail_id
                                FROM main.zkill_killmails k
                                JOIN pilot_floor pf
                                  ON k.victim_character_id = pf.character_id
                                 AND k.kill_time_utc = pf.floor_kill_time_utc
                                WHERE k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList})
                                UNION
                                SELECT k.killmail_id
                                FROM main.zkill_killmail_attackers a
                                JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                                JOIN pilot_floor pf
                                  ON a.character_id = pf.character_id
                                 AND k.kill_time_utc = pf.floor_kill_time_utc
                                WHERE k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList});
                                """;
            buildProtectedFloor.ExecuteNonQuery();
        }

        using (var deleteAttackers = scope.Connection.CreateCommand())
        {
            deleteAttackers.Transaction = scope.Transaction;
            deleteAttackers.CommandText = $"""
                                DELETE FROM main.zkill_killmail_attackers
                                WHERE killmail_id IN (
                                    SELECT killmail_id
                                    FROM main.zkill_killmails
                                    WHERE is_qualifying = 0
                                      AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)}
                                      AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids)
                                );
                                """;
            deleteAttackers.ExecuteNonQuery();
        }

        using (var deleteKillmails = scope.Connection.CreateCommand())
        {
            deleteKillmails.Transaction = scope.Transaction;
            deleteKillmails.CommandText = $"""
                                DELETE FROM main.zkill_killmails
                                WHERE is_qualifying = 0
                                  AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)}
                                  AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids);
                                """;
            deleteKillmails.ExecuteNonQuery();
        }

        using (var dropProtectedFloor = scope.Connection.CreateCommand())
        {
            dropProtectedFloor.Transaction = scope.Transaction;
            dropProtectedFloor.CommandText = "DROP TABLE IF EXISTS temp.protected_killmail_ids;";
            dropProtectedFloor.ExecuteNonQuery();
        }

        scope.Commit();

        return Task.CompletedTask;
    }
}
