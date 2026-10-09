using System.Text;
using Microsoft.Data.Sqlite;
using Killright.Shared.Data;
using Killright.Shared.Killmails;
using Killright.Storage.Scan;

namespace Killright.Storage.Killmails;

internal static class KillmailBulkWriter
{
    private const int ChunkSize = 500;

    public static void Write(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PendingKillmails> pending,
        int qualificationFleetThreshold,
        DateTimeOffset cachedAtUtc)
    {
        if (pending.Count == 0)
            return;

        var known = ReadExistingQualifyingFlags(
            connection,
            transaction,
            pending.SelectMany(item => item.Killmails.Select(killmail => killmail.KillmailId)).Distinct().ToList());

        var newKillmails = new List<(RawKillmail Killmail, int UniqueAttackerCount, bool IsQualifying)>();
        var toQualify = new HashSet<long>();
        var attackers = new Dictionary<(long KillmailId, long CharacterId), KillmailAttacker>();

        foreach (var item in pending)
        {
            foreach (var killmail in item.Killmails)
            {
                var uniqueAttackerCount = KillmailQualification.CountUniqueAttackers(killmail.Attackers);
                var isPodKill = KillmailQualification.IsPodKill(killmail.VictimShipTypeId);
                var isQualifying = KillmailQualification.IsQualifying(uniqueAttackerCount, isPodKill, qualificationFleetThreshold);

                bool attackerRowsCoverAllAttackers;

                if (!known.TryGetValue(killmail.KillmailId, out var existingQualifying))
                {
                    newKillmails.Add((killmail, uniqueAttackerCount, isQualifying));
                    known[killmail.KillmailId] = isQualifying;
                    attackerRowsCoverAllAttackers = isQualifying;
                }
                else
                {
                    if (!existingQualifying && isQualifying)
                    {
                        toQualify.Add(killmail.KillmailId);
                        known[killmail.KillmailId] = true;
                    }

                    attackerRowsCoverAllAttackers = existingQualifying || isQualifying;
                }

                var attackersToConsider = attackerRowsCoverAllAttackers
                    ? killmail.Attackers.Where(attacker => attacker.CharacterId is not null)
                    : killmail.Attackers.Where(attacker => attacker.CharacterId == item.CharacterId);

                foreach (var attacker in attackersToConsider)
                    attackers.TryAdd((killmail.KillmailId, attacker.CharacterId!.Value), attacker);
            }
        }

        InsertKillmails(connection, transaction, newKillmails, cachedAtUtc);
        SetQualifying(connection, transaction, toQualify);
        InsertAttackers(connection, transaction, attackers);
    }

    private static Dictionary<long, bool> ReadExistingQualifyingFlags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<long> killmailIds)
    {
        var known = new Dictionary<long, bool>();

        foreach (var chunk in killmailIds.Chunk(ChunkSize))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                  SELECT killmail_id, is_qualifying
                                  FROM main.zkill_killmails
                                  WHERE killmail_id IN ({string.Join(", ", chunk)});
                                  """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
                known[reader.GetInt64(0)] = reader.GetBoolean(1);
        }

        return known;
    }

    private static void InsertKillmails(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<(RawKillmail Killmail, int UniqueAttackerCount, bool IsQualifying)> newKillmails,
        DateTimeOffset cachedAtUtc)
    {
        foreach (var chunk in newKillmails.Chunk(ChunkSize))
        {
            var values = new StringBuilder();

            foreach (var (killmail, uniqueAttackerCount, isQualifying) in chunk)
            {
                if (values.Length > 0)
                    values.Append(", ");

                values.Append(
                    $"({killmail.KillmailId}, {SqlValueFormatter.String(killmail.KillmailHash)}, {SqlValueFormatter.Date(killmail.KillTimeUtc)}, {killmail.SystemId}, {SqlValueFormatter.Long(killmail.LocationId)}, {SqlValueFormatter.Long(killmail.VictimCharacterId)}, {SqlValueFormatter.Long(killmail.VictimShipTypeId)}, {uniqueAttackerCount}, {SqlValueFormatter.Bool(killmail.IsSolo)}, {SqlValueFormatter.Bool(killmail.IsNpc)}, {SqlValueFormatter.Bool(isQualifying)}, {SqlValueFormatter.Date(cachedAtUtc)})");
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                  INSERT INTO main.zkill_killmails (
                                      killmail_id, killmail_hash, kill_time_utc, system_id, location_id,
                                      victim_character_id, victim_ship_type_id, unique_attacker_count,
                                      is_solo, is_npc, is_qualifying, cached_at_utc
                                  ) VALUES {values}
                                  ON CONFLICT DO NOTHING;
                                  """;
            command.ExecuteNonQuery();
        }
    }

    private static void SetQualifying(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<long> killmailIds)
    {
        foreach (var chunk in killmailIds.Chunk(ChunkSize))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                  UPDATE main.zkill_killmails
                                  SET is_qualifying = 1
                                  WHERE killmail_id IN ({string.Join(", ", chunk)});
                                  """;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertAttackers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<(long KillmailId, long CharacterId), KillmailAttacker> attackers)
    {
        foreach (var chunk in attackers.Chunk(ChunkSize))
        {
            var values = string.Join(
                ", ",
                chunk.Select(pair =>
                    $"({pair.Key.KillmailId}, {pair.Key.CharacterId}, {SqlValueFormatter.Long(pair.Value.CorporationId)}, {SqlValueFormatter.Long(pair.Value.AllianceId)}, {SqlValueFormatter.Long(pair.Value.ShipTypeId)}, {SqlValueFormatter.Long(pair.Value.WeaponTypeId)})"));

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                  INSERT INTO main.zkill_killmail_attackers (
                                      killmail_id, character_id, corporation_id, alliance_id, ship_type_id, weapon_type_id
                                  ) VALUES {values}
                                  ON CONFLICT DO NOTHING;
                                  """;
            command.ExecuteNonQuery();
        }
    }
}
