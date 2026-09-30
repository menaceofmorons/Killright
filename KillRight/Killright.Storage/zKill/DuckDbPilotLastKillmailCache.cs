using DuckDB.NET.Data;
using Killright.Shared.Data;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Killmails;

namespace Killright.Storage.zKill;

public sealed class DuckDbPilotLastKillmailCache : IPilotLastKillmailCache
{
    private readonly KillRightDatabase _database;

    public DuckDbPilotLastKillmailCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<PilotLastKillmailRecord?> GetAsync(long characterId, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT has_killmail,
                                     killmail_id,
                                     kill_time_utc,
                                     activity_type,
                                     system_id,
                                     ship_type_id,
                                     victim_ship_type_id,
                                     attacker_count,
                                     weapon_type_id,
                                     checked_at_utc
                              FROM main.pilot_last_killmail_cache
                              WHERE character_id = {characterId}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<PilotLastKillmailRecord?>(null);

        var checkedAtUtc = reader.GetDateTimeOffset(9);

        if (!reader.GetBoolean(0))
            return Task.FromResult<PilotLastKillmailRecord?>(
                new PilotLastKillmailRecord(characterId, false, null, null, checkedAtUtc));

        var activityType = Enum.TryParse<zKillActivityType>(reader.GetNullableString(3), out var parsed)
            ? parsed
            : zKillActivityType.Kill;

        var killmail = new PilotRecentKillmail(
            reader.GetDateTimeOffset(2),
            activityType,
            reader.GetInt64(4),
            reader.GetNullableInt64(5),
            reader.GetNullableInt64(6),
            reader.GetNullableInt32(7),
            reader.GetNullableInt64(8));

        return Task.FromResult<PilotLastKillmailRecord?>(
            new PilotLastKillmailRecord(characterId, true, reader.GetNullableInt64(1), killmail, checkedAtUtc));
    }

    public Task UpsertAsync(PilotLastKillmailRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = $"DELETE FROM main.pilot_last_killmail_cache WHERE character_id = {record.CharacterId};";
            deleteCommand.ExecuteNonQuery();
        }

        var killmail = record.HasKillmail ? record.Killmail : null;

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = $"""
                                        INSERT INTO main.pilot_last_killmail_cache (
                                            character_id,
                                            has_killmail,
                                            killmail_id,
                                            kill_time_utc,
                                            activity_type,
                                            system_id,
                                            ship_type_id,
                                            victim_ship_type_id,
                                            attacker_count,
                                            weapon_type_id,
                                            checked_at_utc
                                        ) VALUES (
                                            {record.CharacterId},
                                            {SqlValueFormatter.Bool(killmail is not null)},
                                            {SqlValueFormatter.Long(killmail is null ? null : record.KillmailId)},
                                            {SqlValueFormatter.Date(killmail?.KillTimeUtc)},
                                            {SqlValueFormatter.String(killmail?.ActivityType.ToString())},
                                            {SqlValueFormatter.Long(killmail?.SystemId)},
                                            {SqlValueFormatter.Long(killmail?.ShipTypeId)},
                                            {SqlValueFormatter.Long(killmail?.VictimShipTypeId)},
                                            {SqlValueFormatter.Int(killmail?.AttackerCount)},
                                            {SqlValueFormatter.Long(killmail?.WeaponTypeId)},
                                            {SqlValueFormatter.Date(record.CheckedAtUtc)}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }
}
