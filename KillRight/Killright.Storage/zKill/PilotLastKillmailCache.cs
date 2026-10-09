using Microsoft.Data.Sqlite;
using Killright.Shared.Data;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Killmails;

namespace Killright.Storage.zKill;

public sealed class PilotLastKillmailCache : IPilotLastKillmailCache
{
    private readonly KillRightDatabase _database;

    public PilotLastKillmailCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<PilotLastKillmailRecord?> GetAsync(long characterId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();

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

        var checkedAtUtc = reader.GetUtcDateTimeOffset(9);

        if (!reader.GetBoolean(0))
            return Task.FromResult<PilotLastKillmailRecord?>(
                new PilotLastKillmailRecord(characterId, false, null, null, checkedAtUtc));

        var activityType = Enum.TryParse<zKillActivityType>(reader.GetNullableString(3), out var parsed)
            ? parsed
            : zKillActivityType.Kill;

        var killmail = new PilotRecentKillmail(
            reader.GetUtcDateTimeOffset(2),
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
        using var scope = _database.BeginWrite();

        using (var deleteCommand = scope.Connection.CreateCommand())
        {
            deleteCommand.Transaction = scope.Transaction;
            deleteCommand.CommandText = $"DELETE FROM main.pilot_last_killmail_cache WHERE character_id = {record.CharacterId};";
            deleteCommand.ExecuteNonQuery();
        }

        var killmail = record.HasKillmail ? record.Killmail : null;

        using (var insertCommand = scope.Connection.CreateCommand())
        {
            insertCommand.Transaction = scope.Transaction;
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

        scope.Commit();

        return Task.CompletedTask;
    }
}
