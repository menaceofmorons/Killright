using DuckDB.NET.Data;
using PilotIntel.Integration.zKill;
using PilotIntel.Shared.Data;
using PilotIntel.Storage.Database;
using PilotIntel.Shared.Time;

namespace PilotIntel.Storage.zKill;


public sealed class DuckDbzKillActivityCache : IzKillActivityCache
{
    private readonly PilotIntelDatabase _database;

    public DuckDbzKillActivityCache(PilotIntelDatabase database)
    {
        _database = database;
    }

    public Task<zKillActivity?> GetAsync(long characterId, TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT character_id,
                                     has_public_activity_data,
                                     kills_week,
                                     solo_week,
                                     last_active_utc,
                                     last_activity_type,
                                     checked_at_utc,
                                     error
                              FROM zkill_activity_cache
                              WHERE character_id = {characterId}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<zKillActivity?>(null);

        var checkedAtUtc = reader.GetDateTimeOffset(6);

        if (ApplicationClock.UtcNow - checkedAtUtc > maximumAge)
            return Task.FromResult<zKillActivity?>(null);

        var record = new zKillActivityCacheRecord
        {
            CharacterId = reader.GetInt64(0),
            HasPublicActivityData = reader.GetBoolean(1),
            KillsWeek = reader.GetNullableInt32(2),
            SoloWeek = reader.GetNullableInt32(3),
            LastActiveUtc = reader.GetNullableDateTimeOffset(4),
            LastActivityType = ReadActivityTypeOrNull(reader.GetNullableString(5)),
            CheckedAtUtc = checkedAtUtc,
            Error = reader.GetNullableString(7)
        };

        return Task.FromResult<zKillActivity?>(record.ToActivity());
    }

    public Task UpsertAsync(zKillActivity activity, CancellationToken cancellationToken = default)
    {
        var record = zKillActivityCacheRecord.FromActivity(activity);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = $"DELETE FROM zkill_activity_cache WHERE character_id = {record.CharacterId};";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = $"""
                                        INSERT INTO zkill_activity_cache (
                                            character_id,
                                            has_public_activity_data,
                                            kills_week,
                                            solo_week,
                                            last_active_utc,
                                            last_activity_type,
                                            checked_at_utc,
                                            error
                                        ) VALUES (
                                            {record.CharacterId},
                                            {SqlValueFormatter.Bool(record.HasPublicActivityData)},
                                            {SqlValueFormatter.Int(record.KillsWeek)},
                                            {SqlValueFormatter.Int(record.SoloWeek)},
                                            {SqlValueFormatter.Date(record.LastActiveUtc)},
                                            {SqlValueFormatter.String(record.LastActivityType?.ToString())},
                                            {SqlValueFormatter.Date(record.CheckedAtUtc)},
                                            {SqlValueFormatter.String(record.Error)}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    private static zKillActivityType? ReadActivityTypeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse<zKillActivityType>(value, out var result) ? result : null;
    }
}