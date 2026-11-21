using System.Diagnostics;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;
using Xunit.Abstractions;

namespace Killright.Storage.Tests;

public sealed class PerformanceVerificationTests(ITestOutputHelper output)
{
    private const int KillmailCount = 50_000;
    private const int AttackersPerKillmail = 5;

    [Fact]
    public async Task LargeDatabase_StartupQuickCheckAndShutdownFingerprint_AreTimed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"performanceVerification.{Guid.NewGuid():N}.db");
        var folder = Path.Combine(Path.GetTempPath(), $"performanceVerificationBackups.{Guid.NewGuid():N}");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        Generate(database);
        var service = new DatabaseBackupService(database, folder, 2);

        var quickCheck = Time(() => Assert.False(database.QuarantineIfCorrupt()));
        var firstBackup = await TimeAsync(() => service.BackupAsync());
        var fingerprintOnly = await TimeAsync(() => service.BackupAsync());

        var sizeMb = new FileInfo(path).Length / (1024.0 * 1024.0);
        output.WriteLine($"generated: {KillmailCount} killmails, {KillmailCount * AttackersPerKillmail} attacker rows, {sizeMb:F1} MB");
        output.WriteLine($"startup quick_check: {quickCheck.TotalMilliseconds:F0} ms");
        output.WriteLine($"first backup (fingerprint + copy): {firstBackup.TotalMilliseconds:F0} ms");
        output.WriteLine($"shutdown fingerprint, unchanged (skip): {fingerprintOnly.TotalMilliseconds:F0} ms");

        Assert.Single(Directory.GetFiles(folder, "KillRight-*.db"));
    }

    private static void Generate(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var killmails = connection.CreateCommand();
        killmails.Transaction = transaction;
        killmails.CommandText = $"""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {KillmailCount})
            INSERT INTO main.zkill_killmails (
                killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id,
                victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc)
            SELECT 900000 + i, 'hash' || i, 1790553600 + i, 30000142, 40000001, 95000000 + i % 5000,
                587, {AttackersPerKillmail}, 0, 0, i % 3 = 0, 1790553600 + i FROM n;
            """;
        killmails.ExecuteNonQuery();

        using var attackers = connection.CreateCommand();
        attackers.Transaction = transaction;
        attackers.CommandText = $"""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {KillmailCount}),
                a(j) AS (SELECT 1 UNION ALL SELECT j + 1 FROM a WHERE j < {AttackersPerKillmail})
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id)
            SELECT 900000 + i, 96000000 + (i * 7 + j) % 20000, 98000000 + j, NULL, 11567 FROM n, a;
            """;
        attackers.ExecuteNonQuery();

        transaction.Commit();
    }

    private static TimeSpan Time(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();

        return watch.Elapsed;
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var watch = Stopwatch.StartNew();
        await action();

        return watch.Elapsed;
    }
}
