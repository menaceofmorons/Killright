using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;

namespace KillRight.DuckDbMigration.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "krmig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class SourceFixture
{
    public static string Create(
        string path,
        int schemaVersion = 3,
        bool alphaLock = true,
        int? threshold = 5,
        bool corruptLastKillmailTime = false)
    {
        using var connection = new DuckDBConnection($"Data Source={path}");
        connection.Open();

        foreach (var statement in Schema)
            Execute(connection, statement);

        var thresholdSql = threshold?.ToString() ?? "NULL";
        var alphaSql = alphaLock ? "TRUE" : "FALSE";
        var lastKillmailTime = corruptLastKillmailTime ? "'not-a-time'" : "'2026-09-28T18:39:03.5000000Z'";

        Execute(connection, $"INSERT INTO main.schema_metadata VALUES ({schemaVersion}, {thresholdSql}, {alphaSql});");

        Execute(connection, """
            INSERT INTO main.zkill_killmails VALUES
            (138772243, 'hash-a', '2026-09-28T20:39:03.987+02:00', 30001586, 60003760, 2116955190, 587, 3, FALSE, FALSE, TRUE, '2026-09-28T18:40:00.1234567Z'),
            (138772244, NULL, '2026-09-28T18:41:00.0000000Z', 30000142, NULL, NULL, NULL, 1, TRUE, TRUE, FALSE, '2026-09-28T18:42:00.0000000Z');
            """);

        Execute(connection, """
            INSERT INTO main.zkill_killmail_attackers VALUES
            (138772243, 2116955190, 98000001, NULL, 11567, 3138),
            (138772243, 2112625428, NULL, 99000001, NULL, NULL),
            (138772244, 2116955190, 98000001, NULL, 11567, NULL),
            (999999999, 2116955190, 98000001, NULL, 11567, NULL);
            """);

        Execute(connection, """
            INSERT INTO main.zkill_activity_cache VALUES
            (2116955190, TRUE, 12, 4, '2026-09-28T18:39:03.9990000Z', 'Kill', '2026-09-28T19:00:00.0000000Z', NULL, '2026-09-28T19:00:01.0000000Z', '2026-09-21T19:00:01.0000000Z', '2026-09-28T18:39:03.0000000Z'),
            (2112625428, FALSE, NULL, NULL, NULL, NULL, '2026-09-28T19:05:00.0000000Z', 'timeout', NULL, NULL, NULL);
            """);

        Execute(connection, """
            INSERT INTO main.pilot_identity_cache VALUES
            ('T''ral Vsengne', 2112625428, 'T''ral Vsengne', 'Verified', -4.5, 98000001, 'Corp O''Neil', 'CO''N', NULL, NULL, NULL, '2026-09-28 18:39:03.987654', DATE '2008-03-14', '2026-09-28 18:39:03', 500001),
            ('Zoë Müller 漢字', NULL, NULL, 'NotFound', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-09-28 18:50:00', NULL, NULL, NULL);
            """);

        Execute(connection, "INSERT INTO main.esi_entity_name_cache VALUES (98000001, 'corporation', 'Corp O''Neil Ünïcode'), (99000001, 'alliance', 'Alliance 漢字');");

        Execute(connection, """
            INSERT INTO main.zkill_statistics_cache VALUES
            (2116955190, 100, 25, 0.25, 4.75, 10, 2, 'Solo', '2026-09-28T19:10:00.0000000Z', TRUE, NULL, 3, 1),
            (2112625428, 0, 0, 0, 0, 0, 0, 'Unknown', '2026-09-28T19:11:00.0000000Z', FALSE, TRUE, NULL, NULL);
            """);

        Execute(connection, $"""
            INSERT INTO main.pilot_last_killmail_cache VALUES
            (2116955190, TRUE, 138772243, {lastKillmailTime}, 'Kill', 30001586, 623, 587, 3, 3138, '2026-09-28T19:12:00.0000000Z'),
            (2112625428, FALSE, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-09-28T19:13:00.0000000Z');
            """);

        Execute(connection, "CHECKPOINT;");

        return path;
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static readonly string[] Schema =
    [
        "CREATE TABLE main.schema_metadata (schema_version INTEGER NOT NULL, last_qualification_fleet_threshold INTEGER, alpha_lock BOOLEAN DEFAULT FALSE);",
        """
        CREATE TABLE main.zkill_killmails (
            killmail_id BIGINT PRIMARY KEY, killmail_hash TEXT, kill_time_utc TEXT NOT NULL, system_id BIGINT NOT NULL, location_id BIGINT,
            victim_character_id BIGINT, victim_ship_type_id BIGINT, unique_attacker_count INTEGER NOT NULL,
            is_solo BOOLEAN NOT NULL, is_npc BOOLEAN NOT NULL, is_qualifying BOOLEAN NOT NULL, cached_at_utc TEXT NOT NULL);
        """,
        """
        CREATE TABLE main.zkill_killmail_attackers (
            killmail_id BIGINT NOT NULL, character_id BIGINT NOT NULL, corporation_id BIGINT, alliance_id BIGINT, ship_type_id BIGINT,
            weapon_type_id BIGINT, PRIMARY KEY (killmail_id, character_id));
        """,
        """
        CREATE TABLE main.zkill_activity_cache (
            character_id BIGINT PRIMARY KEY, has_public_activity_data BOOLEAN NOT NULL, kills_week INTEGER, solo_week INTEGER,
            last_active_utc TEXT, last_activity_type TEXT, checked_at_utc TEXT NOT NULL, error TEXT, last_recent_call_utc TEXT,
            recent_coverage_start_utc TEXT, last_kill_utc TEXT);
        """,
        """
        CREATE TABLE main.pilot_identity_cache (
            input_name TEXT PRIMARY KEY, character_id BIGINT, character_name TEXT, verify_status TEXT NOT NULL, security_status DOUBLE,
            corporation_id BIGINT, corporation_name TEXT, corporation_ticker TEXT, alliance_id BIGINT, alliance_name TEXT,
            alliance_ticker TEXT, cached_at_utc TIMESTAMP NOT NULL, birthday DATE, security_status_at_utc TIMESTAMP, faction_id BIGINT);
        """,
        "CREATE TABLE main.esi_entity_name_cache (entity_id BIGINT PRIMARY KEY, entity_type TEXT NOT NULL, name TEXT NOT NULL);",
        """
        CREATE TABLE main.zkill_statistics_cache (
            character_id BIGINT PRIMARY KEY, ships_destroyed INTEGER NOT NULL, solo_kills INTEGER NOT NULL, solo_ratio DOUBLE NOT NULL,
            avg_gang_size DOUBLE NOT NULL, ships_lost INTEGER NOT NULL, solo_losses INTEGER NOT NULL, general_style TEXT NOT NULL,
            checked_at_utc TEXT NOT NULL, months_processed BOOLEAN, no_history_marker BOOLEAN, pod_kills INTEGER, pod_losses INTEGER);
        """,
        """
        CREATE TABLE main.pilot_last_killmail_cache (
            character_id BIGINT PRIMARY KEY, has_killmail BOOLEAN NOT NULL, killmail_id BIGINT, kill_time_utc TEXT, activity_type TEXT,
            system_id BIGINT, ship_type_id BIGINT, victim_ship_type_id BIGINT, attacker_count INTEGER, weapon_type_id BIGINT,
            checked_at_utc TEXT NOT NULL);
        """
    ];
}
