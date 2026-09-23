using DuckDB.NET.Data;
using Killright.Shared.Data;
using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public sealed class DuckDbKillmailStore : IKillmailStore
{
    private readonly KillRightDatabase _database;

    public DuckDbKillmailStore(KillRightDatabase database)
    {
        _database = database;
    }

    public Task UpsertAsync(
        long characterId,
        IReadOnlyList<RawKillmail> killmails,
        CancellationToken cancellationToken = default)
    {
        if (killmails.Count == 0)
            return Task.CompletedTask;

        var cachedAtUtc = ApplicationClock.UtcNow;

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        foreach (var killmail in killmails)
        {
            var uniqueAttackerCount = KillmailQualification.CountUniqueAttackers(killmail.Attackers);
            var isPodKill = KillmailQualification.IsPodKill(killmail.VictimShipTypeId);
            var isQualifying = KillmailQualification.IsQualifying(uniqueAttackerCount, isPodKill);

            var existingQualifying = GetExistingQualifyingFlag(connection, transaction, killmail.KillmailId);

            if (existingQualifying is null)
            {
                InsertKillmail(connection, transaction, killmail, uniqueAttackerCount, isQualifying, cachedAtUtc);

                var attackersToWrite = isQualifying
                    ? killmail.Attackers.Where(a => a.CharacterId is not null)
                    : killmail.Attackers.Where(a => a.CharacterId == characterId);

                foreach (var attacker in attackersToWrite)
                    InsertAttackerIfMissing(connection, transaction, killmail.KillmailId, attacker);

                continue;
            }

            if (existingQualifying.Value == false && isQualifying)
                SetQualifying(connection, transaction, killmail.KillmailId);

            var attackerRowsCoverAllAttackers = existingQualifying.Value || isQualifying;

            var attackersToConsider = attackerRowsCoverAllAttackers
                ? killmail.Attackers.Where(a => a.CharacterId is not null)
                : killmail.Attackers.Where(a => a.CharacterId == characterId);

            foreach (var attacker in attackersToConsider)
                InsertAttackerIfMissing(connection, transaction, killmail.KillmailId, attacker);
        }

        transaction.Commit();

        return Task.CompletedTask;
    }

    private static bool? GetExistingQualifyingFlag(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        long killmailId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
                              SELECT is_qualifying
                              FROM main.zkill_killmails
                              WHERE killmail_id = {killmailId};
                              """;

        var value = command.ExecuteScalar();

        return value is not null && value is not DBNull
            ? Convert.ToBoolean(value)
            : null;
    }

    private static void InsertKillmail(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        RawKillmail killmail,
        int uniqueAttackerCount,
        bool isQualifying,
        DateTimeOffset cachedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO main.zkill_killmails (
                killmail_id,
                killmail_hash,
                kill_time_utc,
                system_id,
                location_id,
                victim_character_id,
                victim_ship_type_id,
                unique_attacker_count,
                is_solo,
                is_npc,
                is_qualifying,
                cached_at_utc
            ) VALUES (
                {killmail.KillmailId},
                {SqlValueFormatter.String(killmail.KillmailHash)},
                {SqlValueFormatter.Date(killmail.KillTimeUtc)},
                {killmail.SystemId},
                {SqlValueFormatter.Long(killmail.LocationId)},
                {SqlValueFormatter.Long(killmail.VictimCharacterId)},
                {SqlValueFormatter.Long(killmail.VictimShipTypeId)},
                {uniqueAttackerCount},
                {SqlValueFormatter.Bool(killmail.IsSolo)},
                {SqlValueFormatter.Bool(killmail.IsNpc)},
                {SqlValueFormatter.Bool(isQualifying)},
                {SqlValueFormatter.Date(cachedAtUtc)}
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void SetQualifying(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        long killmailId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
                              UPDATE main.zkill_killmails
                              SET is_qualifying = TRUE
                              WHERE killmail_id = {killmailId};
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertAttackerIfMissing(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        long killmailId,
        KillmailAttacker attacker)
    {
        if (attacker.CharacterId is not long characterId)
            return;

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = $"""
                              SELECT COUNT(*)
                              FROM main.zkill_killmail_attackers
                              WHERE killmail_id = {killmailId} AND character_id = {characterId};
                              """;

        var count = existsCommand.ExecuteScalar();

        if (count is not null && count is not DBNull && Convert.ToInt64(count) > 0)
            return;

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = $"""
            INSERT INTO main.zkill_killmail_attackers (
                killmail_id,
                character_id,
                corporation_id,
                alliance_id,
                ship_type_id
            ) VALUES (
                {killmailId},
                {characterId},
                {SqlValueFormatter.Long(attacker.CorporationId)},
                {SqlValueFormatter.Long(attacker.AllianceId)},
                {SqlValueFormatter.Long(attacker.ShipTypeId)}
            );
            """;
        insertCommand.ExecuteNonQuery();
    }
}
