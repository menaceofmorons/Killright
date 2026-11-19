using Killright.Storage.Database;
using Killright.UI.Diagnostics;
using Xunit;

namespace Killright.UI.Tests.Diagnostics;

public sealed class DeveloperToolTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;

    [Fact]
    public void ClearActivityCache_KeepsCallTimeAndCoverageStart_AndClearsTheListedFields()
    {
        var (database, operations) = Create();
        Execute(database, $"""
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, last_active_utc, last_activity_type, checked_at_utc, last_recent_call_utc, recent_coverage_start_utc, last_kill_utc) VALUES
                ({Lukas}, 1, 1790000000, 'Kill', 1790553600, 1790500000, 1780000000, 1790000000);
            INSERT INTO main.pilot_last_killmail_cache (character_id, has_killmail, checked_at_utc) VALUES ({Lukas}, 0, 1790553600);
            INSERT INTO main.zkill_statistics_cache (character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc, months_processed, no_history_marker, pod_kills, pod_losses) VALUES
                ({Lukas}, 100, 20, 0.25, 4.5, 10, 1, 'Gang', 1790553600, 1, 0, 0, 0);
            """);

        operations.ClearActivityCache();

        Assert.Equal(1790500000L, Scalar(database, "SELECT last_recent_call_utc FROM main.zkill_activity_cache"));
        Assert.Equal(1780000000L, Scalar(database, "SELECT recent_coverage_start_utc FROM main.zkill_activity_cache"));
        Assert.Null(Scalar(database, "SELECT last_active_utc FROM main.zkill_activity_cache"));
        Assert.Null(Scalar(database, "SELECT last_kill_utc FROM main.zkill_activity_cache"));
        Assert.Equal(0L, Scalar(database, "SELECT COUNT(*) FROM main.pilot_last_killmail_cache"));
        Assert.Null(Scalar(database, "SELECT months_processed FROM main.zkill_statistics_cache"));
    }

    [Fact]
    public void ClearActivityCache_RemovesRowsThatHoldNoCallTimeOrCoverage()
    {
        var (database, operations) = Create();
        Execute(database, $"""
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc, last_kill_utc) VALUES
                ({Tral}, 1, 1790553600, 1790553600);
            """);

        operations.ClearActivityCache();

        Assert.Equal(0L, Scalar(database, "SELECT COUNT(*) FROM main.zkill_activity_cache"));
    }

    [Fact]
    public void ClearKillmailCache_ResetsCallTime_AndKeepsCoverageStartAndQualifyingKillmails()
    {
        var (database, operations) = Create();
        Execute(database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'h1', 1790553600, 30000142, NULL, 777, 587, 2, 0, 0, 1, 1790553600),
                (2, 'h2', 1788220800, 30000142, NULL, {Lukas}, 587, 1, 1, 0, 0, 1788220800);
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc, last_recent_call_utc, recent_coverage_start_utc) VALUES
                ({Lukas}, 1, 1790553600, 1790500000, 1780000000);
            """);

        operations.ClearKillmailCache();

        Assert.Equal(1L, Scalar(database, "SELECT COUNT(*) FROM main.zkill_killmails"));
        Assert.Null(Scalar(database, "SELECT last_recent_call_utc FROM main.zkill_activity_cache"));
        Assert.Equal(1780000000L, Scalar(database, "SELECT recent_coverage_start_utc FROM main.zkill_activity_cache"));
    }

    [Fact]
    public void Run_FailingOperation_IsRecordedAndReported_AndDoesNotThrow()
    {
        var recorded = new List<string>();

        var failure = DeveloperToolGuard.Run(() => throw new InvalidOperationException("disk gone"), "Clear cache", recorded.Add);

        Assert.NotNull(failure);
        Assert.Contains("disk gone", failure);
        Assert.Contains("Clear cache", failure);
        Assert.Single(recorded);
        Assert.Contains("disk gone", recorded[0]);
    }

    [Fact]
    public void Run_SucceedingOperation_ReportsNothing()
    {
        var recorded = new List<string>();

        var failure = DeveloperToolGuard.Run(() => { }, "Clear cache", recorded.Add);

        Assert.Null(failure);
        Assert.Empty(recorded);
    }

    [Fact]
    public async Task RunAsync_FailingOperation_IsRecordedAndReported_AndDoesNotThrow()
    {
        var recorded = new List<string>();

        var failure = await DeveloperToolGuard.RunAsync(
            () => Task.FromException(new InvalidOperationException("engine down")),
            "Threat diagnostics",
            recorded.Add);

        Assert.Contains("engine down", failure);
        Assert.Single(recorded);
    }

    [Fact]
    public async Task RunAsync_RecorderThatThrows_StillReportsTheFailure()
    {
        var failure = await DeveloperToolGuard.RunAsync(
            () => throw new InvalidOperationException("boom"),
            "Remove expired killmails",
            _ => throw new IOException("log locked"),
            offUiThread: true);

        Assert.Contains("boom", failure);
    }

    [Fact]
    public async Task RunAsync_OffUiThread_RunsTheWorkOnADifferentThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var workThread = -1;

        var failure = await DeveloperToolGuard.RunAsync(
            () =>
            {
                workThread = Environment.CurrentManagedThreadId;
                return Task.CompletedTask;
            },
            "Remove expired killmails",
            _ => { },
            offUiThread: true);

        Assert.Null(failure);
        Assert.NotEqual(callerThread, workThread);
    }

    private static (KillRightDatabase Database, DeveloperCacheOperations Operations) Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"developer.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return (database, new DeveloperCacheOperations(new DiagnosticsDataService(database)));
    }

    private static object? Scalar(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = command.ExecuteScalar();

        return value is DBNull ? null : value;
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
