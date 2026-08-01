using DuckDB.NET.Data;
using PilotIntel.Integration.zKill;
using PilotIntel.Shared.Data;
using PilotIntel.Shared.Killmails;
using PilotIntel.Shared.Time;
using PilotIntel.Storage.Database;

namespace PilotIntel.Storage.Killmails;

public sealed class DuckDbRecentKillmailCache : IRecentKillmailCache
{
    private readonly PilotIntelDatabase _database;

    public DuckDbRecentKillmailCache(PilotIntelDatabase database)
    {
        _database = database;
    }

    public Task<IReadOnlyList<KillmailRecord>> GetForCharacterAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT killmail_id,
                                     killmail_hash,
                                     character_id,
                                     kill_time_utc,
                                     is_loss,
                                     attacker_count,
                                     is_solo,
                                     ship_type_id,
                                     system_id,
                                     location_id,
                                     is_npc,
                                     cached_at_utc
                              FROM main.zkill_recent_killmail_cache
                              WHERE character_id = {characterId}
                              ORDER BY kill_time_utc DESC;
                              """;

        using var reader = command.ExecuteReader();
        var results = new List<KillmailRecord>();

        while (reader.Read())
        {
            results.Add(new KillmailRecord(
                reader.GetInt64(0),
                reader.GetNullableString(1),
                reader.GetInt64(2),
                reader.GetDateTimeOffset(3),
                reader.GetBoolean(4),
                reader.GetInt32(5),
                reader.GetBoolean(6),
                reader.GetNullableInt64(7),
                reader.GetNullableInt64(8),
                reader.GetNullableInt64(9),
                reader.GetBoolean(10),
                reader.GetDateTimeOffset(11)));
        }

        return Task.FromResult<IReadOnlyList<KillmailRecord>>(results);
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
                                     is_loss,
                                     is_solo
                              FROM main.zkill_recent_killmail_cache
                              WHERE character_id = {characterId}
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

    public Task UpsertAsync(
        IReadOnlyList<KillmailRecord> killmails,
        CancellationToken cancellationToken = default)
    {
        if (killmails.Count == 0)
            return Task.CompletedTask;

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        foreach (var killmail in killmails)
        {
            if (KillmailExists(connection, killmail.KillmailId))
                continue;

            using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = $"""
                INSERT INTO main.zkill_recent_killmail_cache (
                    killmail_id,
                    killmail_hash,
                    character_id,
                    kill_time_utc,
                    is_loss,
                    attacker_count,
                    is_solo,
                    ship_type_id,
                    system_id,
                    location_id,
                    is_npc,
                    cached_at_utc
                ) VALUES (
                    {killmail.KillmailId},
                    {SqlValueFormatter.String(killmail.KillmailHash)},
                    {killmail.CharacterId},
                    {SqlValueFormatter.Date(killmail.KillTimeUtc)},
                    {SqlValueFormatter.Bool(killmail.IsLoss)},
                    {killmail.AttackerCount},
                    {SqlValueFormatter.Bool(killmail.IsSolo)},
                    {SqlValueFormatter.Long(killmail.ShipTypeId)},
                    {SqlValueFormatter.Long(killmail.SystemId)},
                    {SqlValueFormatter.Long(killmail.LocationId)},
                    {SqlValueFormatter.Bool(killmail.IsNpc)},
                    {SqlValueFormatter.Date(killmail.CachedAtUtc)}
                );
                """;
            insertCommand.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = ApplicationClock.UtcNow.AddDays(-7);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              DELETE FROM main.zkill_recent_killmail_cache
                              WHERE kill_time_utc < {SqlValueFormatter.Date(cutoffUtc)};
                              """;
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetMostRecentKillmailAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT MAX(kill_time_utc)
                              FROM main.zkill_recent_killmail_cache
                              WHERE character_id = {characterId};
                              """;

        var value = command.ExecuteScalar();

        if (value is null || value is DBNull)
            return Task.FromResult<DateTimeOffset?>(null);

        var text = value.ToString();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<DateTimeOffset?>(null);

        return DateTimeOffset.TryParse(text, out var parsed)
            ? Task.FromResult<DateTimeOffset?>(parsed)
            : Task.FromResult<DateTimeOffset?>(null);
    }

    private static bool KillmailExists(
        DuckDBConnection connection,
        long killmailId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT COUNT(*)
                              FROM main.zkill_recent_killmail_cache
                              WHERE killmail_id = {killmailId};
                              """;

        var value = command.ExecuteScalar();

        return value is not null &&
               value is not DBNull &&
               Convert.ToInt64(value) > 0;
    }
}