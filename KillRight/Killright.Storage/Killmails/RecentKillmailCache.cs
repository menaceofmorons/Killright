using System.Diagnostics;
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
    private const int BatchSize = 200;

    private static readonly string PodShipTypeIdList = KillmailQualification.PodShipTypeIdSqlList;

    private readonly KillRightDatabase _database;
    private readonly int _recentWindowDays;
    private readonly Action<PurgePassStats>? _passCompleted;

    public RecentKillmailCache(
        KillRightDatabase database,
        int recentWindowDays,
        Action<PurgePassStats>? passCompleted = null)
    {
        _database = database;
        _recentWindowDays = recentWindowDays;
        _passCompleted = passCompleted;
    }

    internal Action<int>? BeforeBatch { get; init; }

    internal Action<int>? InsideBatch { get; init; }

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
        try
        {
            RemoveExpired(cancellationToken);

            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled(cancellationToken);
        }
    }

    private void RemoveExpired(CancellationToken cancellationToken)
    {
        var cutoffUtc = ApplicationClock.UtcNow.AddDays(-_recentWindowDays);
        var candidates = ReadCandidates(cutoffUtc);
        var batches = 0;

        for (var offset = 0; offset < candidates.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BeforeBatch?.Invoke(batches);

            var idList = string.Join(",", candidates.Skip(offset).Take(BatchSize));

            using var scope = _database.BeginWrite();

            using (var deleteBatch = scope.Connection.CreateCommand())
            {
                deleteBatch.Transaction = scope.Transaction;
                deleteBatch.CommandText = $"""
                                WITH {BuildProtectedFloorCtes(idList)}
                                DELETE FROM main.zkill_killmails
                                WHERE killmail_id IN ({idList})
                                  AND is_qualifying = 0
                                  AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids);
                                """;
                deleteBatch.ExecuteNonQuery();
            }

            InsideBatch?.Invoke(batches);

            scope.Commit();
            batches++;
        }

        var vacuumTimestamp = Stopwatch.GetTimestamp();

        using (var vacuumScope = _database.BeginWrite())
        {
            using var vacuum = vacuumScope.Connection.CreateCommand();
            vacuum.Transaction = vacuumScope.Transaction;
            vacuum.CommandText = "PRAGMA incremental_vacuum;";
            vacuum.ExecuteNonQuery();

            vacuumScope.Commit();
        }

        ReportPass(new PurgePassStats(
            candidates.Count,
            batches,
            Stopwatch.GetElapsedTime(vacuumTimestamp).TotalMilliseconds));
    }

    private List<long> ReadCandidates(DateTimeOffset cutoffUtc)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              WITH {BuildProtectedFloorCtes(null)}
                              SELECT killmail_id
                              FROM main.zkill_killmails
                              WHERE is_qualifying = 0
                                AND kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)}
                                AND killmail_id NOT IN (SELECT killmail_id FROM protected_killmail_ids)
                              ORDER BY killmail_id;
                              """;

        var candidates = new List<long>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
            candidates.Add(reader.GetInt64(0));

        return candidates;
    }

    private static string BuildProtectedFloorCtes(string? batchIdList)
    {
        var batchPilots = batchIdList is null
            ? string.Empty
            : $"""
               batch_pilots AS (
                   SELECT victim_character_id AS character_id
                   FROM main.zkill_killmails
                   WHERE killmail_id IN ({batchIdList})
                   UNION
                   SELECT character_id
                   FROM main.zkill_killmail_attackers
                   WHERE killmail_id IN ({batchIdList})
               ),
               """;

        var pilotFilter = batchIdList is null
            ? string.Empty
            : "WHERE character_id IN (SELECT character_id FROM batch_pilots)";

        var killmailFilter = batchIdList is null
            ? string.Empty
            : $"AND k.killmail_id IN ({batchIdList})";

        return $"""
                {batchPilots}
                scanned_pilots AS (
                    SELECT character_id
                    FROM main.zkill_activity_cache
                    {pilotFilter}
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
                ),
                protected_killmail_ids AS (
                    SELECT k.killmail_id
                    FROM main.zkill_killmails k
                    JOIN pilot_floor pf
                      ON k.victim_character_id = pf.character_id
                     AND k.kill_time_utc = pf.floor_kill_time_utc
                    WHERE (k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                      {killmailFilter}
                    UNION
                    SELECT k.killmail_id
                    FROM main.zkill_killmail_attackers a
                    JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                    JOIN pilot_floor pf
                      ON a.character_id = pf.character_id
                     AND k.kill_time_utc = pf.floor_kill_time_utc
                    WHERE (k.victim_ship_type_id IS NULL OR k.victim_ship_type_id NOT IN ({PodShipTypeIdList}))
                      {killmailFilter}
                )
                """;
    }

    private void ReportPass(PurgePassStats stats)
    {
        try
        {
            _passCompleted?.Invoke(stats);
        }
        catch
        {
        }
    }
}

public sealed record PurgePassStats(int Candidates, int Batches, double VacuumMilliseconds);
