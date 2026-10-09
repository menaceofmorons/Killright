using Microsoft.Data.Sqlite;
using Killright.Shared.Data;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Identity;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;

namespace Killright.Storage.Scan;

public static class ScanWriter
{
    private const int ChunkSize = 500;

    public static void Commit(
        ScanDatabaseSession session,
        ScanWriteBatch batch,
        int qualificationFleetThreshold,
        DateTimeOffset cachedAtUtc,
        ScanTimings? timings = null,
        string? tag = null)
    {
        if (batch.IsEmpty)
            return;

        using var scope = session.BeginWrite(timings, tag);

        var connection = scope.Connection;
        var transaction = scope.Transaction;

        WriteIdentities(connection, transaction, batch.Identities);
        WriteEntityNames(connection, transaction, batch.EntityNames);
        WriteStatistics(connection, transaction, batch.Statistics);
        KillmailBulkWriter.Write(connection, transaction, batch.Killmails, qualificationFleetThreshold, cachedAtUtc);
        ClearNoHistoryMarkers(connection, transaction, batch.NoHistoryClears);
        WriteActivities(connection, transaction, batch.Activities);

        scope.Commit();
    }

    private static void WriteIdentities(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PilotIdentityCacheRecord> records)
    {
        foreach (var chunk in records.Chunk(ChunkSize))
        {
            var values = string.Join(
                ", ",
                chunk.Select(record =>
                    $"({SqlValueFormatter.String(record.InputName)}, {SqlValueFormatter.Long(record.CharacterId)}, {SqlValueFormatter.String(record.CharacterName)}, {SqlValueFormatter.String(record.VerifyStatus.ToString())}, {SqlValueFormatter.Double(record.SecurityStatus)}, {SqlValueFormatter.Long(record.CorporationId)}, {SqlValueFormatter.String(record.CorporationName)}, {SqlValueFormatter.String(record.CorporationTicker)}, {SqlValueFormatter.Long(record.AllianceId)}, {SqlValueFormatter.String(record.AllianceName)}, {SqlValueFormatter.String(record.AllianceTicker)}, {SqlValueFormatter.Date(record.CachedAtUtc)}, {SqlValueFormatter.Date(record.Birthday)}, {(record.SecurityStatusAtUtc is { } securityStatusAtUtc ? SqlValueFormatter.Date(securityStatusAtUtc) : "NULL")}, {SqlValueFormatter.Long(record.FactionId)})"));

            Execute(
                connection,
                transaction,
                $"""
                INSERT OR REPLACE INTO main.pilot_identity_cache (
                    input_name, character_id, character_name, verify_status, security_status,
                    corporation_id, corporation_name, corporation_ticker,
                    alliance_id, alliance_name, alliance_ticker,
                    cached_at_utc, birthday, security_status_at_utc, faction_id
                ) VALUES {values};
                """);
        }
    }

    private static void WriteEntityNames(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<EsiEntityName> names)
    {
        foreach (var chunk in names.Chunk(ChunkSize))
        {
            var values = string.Join(
                ", ",
                chunk.Select(name => $"({name.EntityId}, {SqlValueFormatter.String(name.EntityType)}, {SqlValueFormatter.String(name.Name)})"));

            Execute(
                connection,
                transaction,
                $"INSERT OR REPLACE INTO main.esi_entity_name_cache (entity_id, entity_type, name) VALUES {values};");
        }
    }

    private static void WriteStatistics(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PendingStatistics> statistics)
    {
        foreach (var chunk in statistics.Chunk(ChunkSize))
        {
            var values = string.Join(
                ", ",
                chunk.Select(item =>
                {
                    var record = zKillStatisticsCacheRecord.FromStatistics(
                        item.CharacterId,
                        item.Statistics,
                        item.GeneralStyle,
                        item.NoHistory,
                        item.CheckedAtUtc);

                    return $"({record.CharacterId}, {record.ShipsDestroyed}, {record.SoloKills}, {SqlValueFormatter.Double(record.SoloRatio)}, {SqlValueFormatter.Double(record.AvgGangSize)}, {record.ShipsLost}, {record.SoloLosses}, {SqlValueFormatter.String(record.GeneralStyle)}, {SqlValueFormatter.Bool(true)}, {SqlValueFormatter.Bool(record.NoHistory)}, {SqlValueFormatter.Date(record.CheckedAtUtc)}, {record.PodKills}, {record.PodLosses})";
                }));

            Execute(
                connection,
                transaction,
                $"""
                INSERT OR REPLACE INTO main.zkill_statistics_cache (
                    character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size,
                    ships_lost, solo_losses, general_style, months_processed, no_history_marker,
                    checked_at_utc, pod_kills, pod_losses
                ) VALUES {values};
                """);
        }
    }

    private static void ClearNoHistoryMarkers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<long> characterIds)
    {
        foreach (var chunk in characterIds.Chunk(ChunkSize))
        {
            Execute(
                connection,
                transaction,
                $"UPDATE main.zkill_statistics_cache SET no_history_marker = 0 WHERE character_id IN ({string.Join(", ", chunk)});");
        }
    }

    private static void WriteActivities(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Killright.Integration.zKill.zKillActivity> activities)
    {
        foreach (var chunk in activities.Chunk(ChunkSize))
        {
            var values = string.Join(
                ", ",
                chunk.Select(activity =>
                {
                    var record = zKillActivityCacheRecord.FromActivity(activity);

                    return $"({record.CharacterId}, {SqlValueFormatter.Bool(record.HasPublicActivityData)}, {SqlValueFormatter.Int(record.KillsWeek)}, {SqlValueFormatter.Int(record.SoloWeek)}, {SqlValueFormatter.Date(record.LastActiveUtc)}, {SqlValueFormatter.String(record.LastActivityType?.ToString())}, {SqlValueFormatter.Date(record.CheckedAtUtc)}, {SqlValueFormatter.String(record.Error)}, {SqlValueFormatter.Date(record.LastSuccessfulRecentCallUtc)}, {SqlValueFormatter.Date(record.RecentCoverageStartUtc)}, {SqlValueFormatter.Date(record.LastKillUtc)})";
                }));

            Execute(
                connection,
                transaction,
                $"""
                INSERT OR REPLACE INTO main.zkill_activity_cache (
                    character_id, has_public_activity_data, kills_week, solo_week,
                    last_active_utc, last_activity_type, checked_at_utc, error,
                    last_recent_call_utc, recent_coverage_start_utc, last_kill_utc
                ) VALUES {values};
                """);
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
