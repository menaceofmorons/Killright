using DuckDB.NET.Data;
using PilotIntel.Shared.zKill;
using PilotIntel.Shared.Data;
using PilotIntel.Shared.Time;
using PilotIntel.Storage.Database;

namespace PilotIntel.Storage.zKill;

public sealed class DuckDbzKillStatisticsCache : IzKillStatisticsCache
{
    private readonly PilotIntelDatabase _database;

    public DuckDbzKillStatisticsCache(PilotIntelDatabase database)
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
                                     checked_at_utc
                              FROM main.zkill_statistics_cache
                              WHERE character_id = {characterId}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<zKillStatistics?>(null);

        var checkedAtUtc = reader.GetDateTimeOffset(8);

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
            CheckedAtUtc = checkedAtUtc
        };

        return Task.FromResult<zKillStatistics?>(record.ToStatistics());
    }

    public Task UpsertAsync(
        long characterId,
        zKillStatistics statistics,
        string generalStyle,
        CancellationToken cancellationToken = default)
    {
        var record = zKillStatisticsCacheRecord.FromStatistics(
            characterId,
            statistics,
            generalStyle,
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
                checked_at_utc
            ) VALUES (
                {record.CharacterId},
                {record.ShipsDestroyed},
                {record.SoloKills},
                {SqlValueFormatter.Double(record.SoloRatio)},
                {SqlValueFormatter.Double(record.AvgGangSize)},
                {record.ShipsLost},
                {record.SoloLosses},
                {SqlValueFormatter.String(record.GeneralStyle)},
                {SqlValueFormatter.Date(record.CheckedAtUtc)}
            );
            """;
        insertCommand.ExecuteNonQuery();

        return Task.CompletedTask;
    }
}