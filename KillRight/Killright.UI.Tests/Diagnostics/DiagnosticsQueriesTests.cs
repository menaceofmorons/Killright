using System.Data;
using System.Reflection;
using Killright.Storage.Database;
using Killright.UI.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.UI.Tests.Diagnostics;

public sealed class DiagnosticsQueriesTests
{
    private const string Lukas = "95465499";
    private const string Tral = "91321792";
    private const string Symptom = "2112625428";

    public static IEnumerable<object[]> QueryNames()
    {
        return Queries().Keys.Select(name => new object[] { name });
    }

    [Theory]
    [MemberData(nameof(QueryNames))]
    public void DiagnosticQuery_RunsAgainstTheSqliteSchema(string name)
    {
        var (database, service) = CreateService();
        Seed(database);

        var sql = Queries()[name].Replace("{{RecentWindowCutoffUtc}}", "1790553600");

        var table = service.LoadRows(sql);

        Assert.NotNull(table);
    }

    [Fact]
    public void RecentKillmailRows_ShowTimesAsReadableUtcText()
    {
        var (database, service) = CreateService();
        Seed(database);

        var table = service.LoadRows(Queries()["Recent Killmail Rows"]);

        Assert.Equal("2026-09-28 00:00:00", Convert.ToString(table.Rows[0]["kill_time_utc"]));
        Assert.Equal("2026-09-28 00:00:00", Convert.ToString(table.Rows[0]["cached_at_utc"]));
    }

    [Fact]
    public void PairsByShareKillCount_ReturnsTheSharedKillAndItsTime()
    {
        var (database, service) = CreateService();
        Seed(database);

        var table = service.LoadRows(Queries()["Group Detection: Pairs By Shared Kill Count"]);

        Assert.Equal(1, table.Rows.Count);
        Assert.Equal(long.Parse(Tral), Convert.ToInt64(table.Rows[0]["pilot_a"]));
        Assert.Equal(long.Parse(Lukas), Convert.ToInt64(table.Rows[0]["pilot_b"]));
        Assert.Equal(1L, Convert.ToInt64(table.Rows[0]["shared_kills"]));
        Assert.Equal("2026-09-28 00:00:00", Convert.ToString(table.Rows[0]["last_shared_kill_utc"]));
    }

    [Fact]
    public void ExpiredStatisticsRows_ListsOnlyRowsOlderThanThirtyDays()
    {
        var (database, service) = CreateService();
        Seed(database);

        var table = service.LoadRows(Queries()["Expired zKill Statistics Cache Rows"]);

        Assert.Equal(1, table.Rows.Count);
        Assert.Equal(long.Parse(Symptom), Convert.ToInt64(table.Rows[0]["character_id"]));
    }

    [Fact]
    public void ExecuteNonQuery_RunsThroughTheWriteGate()
    {
        var (database, service) = CreateService();
        Seed(database);

        service.ExecuteNonQuery("DELETE FROM main.pilot_identity_cache;");
        service.ExecuteNonQuery("UPDATE main.zkill_statistics_cache SET months_processed = NULL;");

        Assert.Equal(0L, Count(database, "pilot_identity_cache"));
        Assert.Equal(0L, Count(database, "zkill_statistics_cache WHERE months_processed IS NOT NULL"));
    }

    private static IReadOnlyDictionary<string, string> Queries()
    {
        var field = typeof(DiagnosticsView).GetField("DiagnosticQueries", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (Dictionary<string, string>)field.GetValue(null)!;
    }

    private static (KillRightDatabase Database, DiagnosticsDataService Service) CreateService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diagnostics.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return (database, new DiagnosticsDataService(database));
    }

    private static void Seed(KillRightDatabase database)
    {
        Execute(database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'h1', 1790553600, 30000142, NULL, 777, 587, 2, 0, 0, 1, 1790553600),
                (2, 'h2', 1788220800, 30000142, NULL, {Lukas}, 587, 1, 1, 0, 0, 1788220800);
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id, weapon_type_id) VALUES
                (1, {Lukas}, 98000001, NULL, 11567, 3074),
                (1, {Tral}, 98000002, 99000001, 17738, 3074),
                (2, {Lukas}, 98000001, NULL, 11567, NULL);
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, corporation_id, corporation_name, corporation_ticker, cached_at_utc, birthday, security_status_at_utc) VALUES
                ('LUKAS NAARII', {Lukas}, 'Lukas Naarii', 'Verified', 1.2, 98000001, 'Corp One', 'ONE', 1790553600, 1205452800, 1790553600),
                ('T''RAL VSENGNE', {Tral}, 'T''ral Vsengne', 'Verified', 0.4, 98000002, 'Corp Two', 'TWO', 1790553600, NULL, NULL);
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc, last_kill_utc) VALUES
                ({Lukas}, 1, 1790553600, 1790553600);
            INSERT INTO main.zkill_statistics_cache (character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc, months_processed, no_history_marker, pod_kills, pod_losses) VALUES
                ({Lukas}, 100, 20, 0.25, 4.5, 10, 1, 'Gang', {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}, 1, 0, 0, 0),
                ({Symptom}, 5, 0, 0.0, 6.0, 1, 0, 'Fleet', {DateTimeOffset.UtcNow.AddDays(-45).ToUnixTimeSeconds()}, 1, 0, 0, 0);
            """);
    }

    private static long Count(KillRightDatabase database, string tableAndFilter)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{tableAndFilter};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
