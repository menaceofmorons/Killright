using DuckDB.NET.Data;
using Killright.Integration.zKill;
using Killright.Shared.Data;
using Killright.Shared.Killmails;
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
                                     is_solo,
                                     CAST(NULL AS BIGINT) AS victim_ship_type_id
                              FROM main.zkill_killmails
                              WHERE victim_character_id = {characterId}
                              UNION ALL
                              SELECT k.kill_time_utc,
                                     FALSE AS is_loss,
                                     k.is_solo,
                                     k.victim_ship_type_id
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
        DateTimeOffset? lastKillUtc = null;

        while (reader.Read())
        {
            var killTimeUtc = reader.GetDateTimeOffset(0);
            var isLoss = reader.GetBoolean(1);
            var isSolo = reader.GetBoolean(2);
            var victimShipTypeId = reader.GetNullableInt64(3);

            if (killTimeUtc < cutoffUtc)
                continue;

            hasPublicActivityData = true;

            var isPodKill = !isLoss && KillmailQualification.IsPodKill(victimShipTypeId);

            if (lastActiveUtc is null && !isPodKill)
            {
                lastActiveUtc = killTimeUtc;
                lastActivityType = isLoss ? zKillActivityType.Loss : zKillActivityType.Kill;
            }

            if (!isLoss && !isPodKill)
            {
                lastKillUtc ??= killTimeUtc;
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
            ApplicationClock.UtcNow,
            LastKillUtc: lastKillUtc));
    }

    public Task<PilotRecentKillmail?> GetMostRecentKillmailAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT kill_time_utc,
                                     TRUE AS is_loss,
                                     system_id,
                                     victim_ship_type_id AS ship_type_id,
                                     CAST(NULL AS BIGINT) AS victim_ship_type_id,
                                     CAST(NULL AS INTEGER) AS attacker_count,
                                     CAST(NULL AS BIGINT) AS weapon_type_id
                              FROM main.zkill_killmails
                              WHERE victim_character_id = {characterId}
                              UNION ALL
                              SELECT k.kill_time_utc,
                                     FALSE AS is_loss,
                                     k.system_id,
                                     a.ship_type_id AS ship_type_id,
                                     k.victim_ship_type_id,
                                     k.unique_attacker_count AS attacker_count,
                                     a.weapon_type_id
                              FROM main.zkill_killmail_attackers a
                              JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                              WHERE a.character_id = {characterId}
                                AND (k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN (
                                    {KillmailQualification.CapsuleShipTypeId},
                                    {KillmailQualification.CapsuleGenolutionShipTypeId}
                                ))
                              ORDER BY kill_time_utc DESC
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<PilotRecentKillmail?>(null);

        var isLoss = reader.GetBoolean(1);

        return Task.FromResult<PilotRecentKillmail?>(new PilotRecentKillmail(
            reader.GetDateTimeOffset(0),
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

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        using (var buildProtectedFloor = connection.CreateCommand())
        {
            buildProtectedFloor.Transaction = transaction;
            buildProtectedFloor.CommandText = """
                                CREATE TEMP TABLE protected_killmail_ids AS
                                WITH scanned_pilots AS (
                                    SELECT character_id
                                    FROM main.zkill_activity_cache
                                ),
                                pilot_kill_times AS (
                                    SELECT victim_character_id AS character_id, kill_time_utc
                                    FROM main.zkill_killmails
                                    WHERE victim_character_id IN (SELECT character_id FROM scanned_pilots)
                                    UNION ALL
                                    SELECT a.character_id, k.kill_time_utc
                                    FROM main.zkill_killmail_attackers a
                                    JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                                    WHERE a.character_id IN (SELECT character_id FROM scanned_pilots)
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
                                UNION
                                SELECT k.killmail_id
                                FROM main.zkill_killmail_attackers a
                                JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                                JOIN pilot_floor pf
                                  ON a.character_id = pf.character_id
                                 AND k.kill_time_utc = pf.floor_kill_time_utc;
                                """;
            buildProtectedFloor.ExecuteNonQuery();
        }

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
                                      AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids)
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
                                  AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)}
                                  AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids);
                                """;
            deleteKillmails.ExecuteNonQuery();
        }

        transaction.Commit();

        return Task.CompletedTask;
    }
}
