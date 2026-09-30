using DuckDB.NET.Data;
using Killright.Shared.zKill;
using Killright.Shared.Data;
using Killright.Shared.Time;
using Killright.Storage.Database;

namespace Killright.Storage.zKill;

public sealed class DuckDbzKillStatisticsCache : IzKillStatisticsCache
{
    private readonly KillRightDatabase _database;

    public DuckDbzKillStatisticsCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<zKillStatistics?> GetAsync(
        long characterId,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT character_id,
                                     ships_destroyed,
                                     solo_kills,
                                     solo_ratio,
                                     avg_gang_size,
                                     ships_lost,
                                     solo_losses,
                                     general_style,
                                     months_processed,
                                     no_history_marker,
                                     checked_at_utc,
                                     pod_kills
                              FROM main.zkill_statistics_cache
                              WHERE character_id = {characterId}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<zKillStatistics?>(null);

        var monthsProcessed = reader.GetNullableBoolean(8);

        if (monthsProcessed != true)
            return Task.FromResult<zKillStatistics?>(null);

        var checkedAtUtc = reader.GetDateTimeOffset(10);

        if (ApplicationClock.UtcNow - checkedAtUtc > maximumAge)
            return Task.FromResult<zKillStatistics?>(null);

        var record = new zKillStatisticsCacheRecord
        {
            CharacterId = reader.GetInt64(0),
            ShipsDestroyed = reader.GetInt32(1),
            SoloKills = reader.GetInt32(2),
            SoloRatio = reader.GetDouble(3),
            AvgGangSize = reader.GetDouble(4),
            ShipsLost = reader.GetInt32(5),
            SoloLosses = reader.GetInt32(6),
            GeneralStyle = reader.GetString(7),
            NoHistory = reader.GetBoolean(9),
            CheckedAtUtc = checkedAtUtc,
            PodKills = reader.GetNullableInt32(11) ?? 0
        };

        return Task.FromResult<zKillStatistics?>(record.ToStatistics());
    }

    public Task<IReadOnlyDictionary<long, zKillStatistics>> GetManyAsync(
        IReadOnlyCollection<long> characterIds,
        TimeSpan maximumAge,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, zKillStatistics>();

        if (characterIds.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<long, zKillStatistics>>(results);

        using var ownedConnection = session is null ? _database.OpenConnection() : null;
        var connection = session?.Connection ?? ownedConnection!;

        foreach (var chunk in characterIds.Distinct().Chunk(500))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  SELECT character_id,
                                         ships_destroyed,
                                         solo_kills,
                                         solo_ratio,
                                         avg_gang_size,
                                         ships_lost,
                                         solo_losses,
                                         general_style,
                                         months_processed,
                                         no_history_marker,
                                         checked_at_utc,
                                         pod_kills
                                  FROM main.zkill_statistics_cache
                                  WHERE character_id IN ({string.Join(", ", chunk)});
                                  """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (reader.GetNullableBoolean(8) != true)
                    continue;

                var checkedAtUtc = reader.GetDateTimeOffset(10);

                if (ApplicationClock.UtcNow - checkedAtUtc > maximumAge)
                    continue;

                var record = new zKillStatisticsCacheRecord
                {
                    CharacterId = reader.GetInt64(0),
                    ShipsDestroyed = reader.GetInt32(1),
                    SoloKills = reader.GetInt32(2),
                    SoloRatio = reader.GetDouble(3),
                    AvgGangSize = reader.GetDouble(4),
                    ShipsLost = reader.GetInt32(5),
                    SoloLosses = reader.GetInt32(6),
                    GeneralStyle = reader.GetString(7),
                    NoHistory = reader.GetBoolean(9),
                    CheckedAtUtc = checkedAtUtc,
                    PodKills = reader.GetNullableInt32(11) ?? 0
                };

                results[record.CharacterId] = record.ToStatistics();
            }
        }

        return Task.FromResult<IReadOnlyDictionary<long, zKillStatistics>>(results);
    }

    public Task UpsertAsync(
        long characterId,
        zKillStatistics statistics,
        string generalStyle,
        bool noHistory,
        CancellationToken cancellationToken = default)
    {
        var record = zKillStatisticsCacheRecord.FromStatistics(
            characterId,
            statistics,
            generalStyle,
            noHistory,
            ApplicationClock.UtcNow);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = $"DELETE FROM main.zkill_statistics_cache WHERE character_id = {record.CharacterId};";
            deleteCommand.ExecuteNonQuery();
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = $"""
            INSERT INTO main.zkill_statistics_cache (
                character_id,
                ships_destroyed,
                solo_kills,
                solo_ratio,
                avg_gang_size,
                ships_lost,
                solo_losses,
                general_style,
                months_processed,
                no_history_marker,
                checked_at_utc,
                pod_kills
            ) VALUES (
                {record.CharacterId},
                {record.ShipsDestroyed},
                {record.SoloKills},
                {SqlValueFormatter.Double(record.SoloRatio)},
                {SqlValueFormatter.Double(record.AvgGangSize)},
                {record.ShipsLost},
                {record.SoloLosses},
                {SqlValueFormatter.String(record.GeneralStyle)},
                {SqlValueFormatter.Bool(true)},
                {SqlValueFormatter.Bool(record.NoHistory)},
                {SqlValueFormatter.Date(record.CheckedAtUtc)},
                {record.PodKills}
            );
            """;
        insertCommand.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task ClearNoHistoryMarkerAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE main.zkill_statistics_cache
            SET no_history_marker = FALSE
            WHERE character_id = {characterId};
            """;
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }
}
