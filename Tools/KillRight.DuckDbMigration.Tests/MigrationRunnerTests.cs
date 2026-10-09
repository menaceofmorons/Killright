using System.Security.Cryptography;
using Killright.Storage.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillRight.DuckDbMigration.Tests;

public sealed class MigrationRunnerTests
{
    private static long Unix(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static object? Scalar(string targetPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={targetPath};Mode=ReadOnly;Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = command.ExecuteScalar();

        return value is DBNull ? null : value;
    }

    private static (int ExitCode, string Output) Run(string source, string target)
    {
        var writer = new StringWriter();
        var exitCode = MigrationRunner.Run(new MigrationOptions(source, target), writer);

        return (exitCode, writer.ToString());
    }

    [Fact]
    public void Run_Fixture_CopiesEveryTableWithConvertedValues()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"));
        var target = directory.File("target.db");

        var (exitCode, output) = Run(source, target);

        Assert.Equal(0, exitCode);
        Assert.Contains("Migration complete", output);

        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM zkill_killmails;"));
        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM zkill_activity_cache;"));
        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM pilot_identity_cache;"));
        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM esi_entity_name_cache;"));
        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM zkill_statistics_cache;"));
        Assert.Equal(2L, Scalar(target, "SELECT COUNT(*) FROM pilot_last_killmail_cache;"));

        Assert.Equal(Unix(2026, 9, 28, 18, 39, 3), Scalar(target, "SELECT kill_time_utc FROM zkill_killmails WHERE killmail_id = 138772243;"));
        Assert.Equal(Unix(2026, 9, 28, 18, 40, 0), Scalar(target, "SELECT cached_at_utc FROM zkill_killmails WHERE killmail_id = 138772243;"));
        Assert.Equal("hash-a", Scalar(target, "SELECT killmail_hash FROM zkill_killmails WHERE killmail_id = 138772243;"));
        Assert.Null(Scalar(target, "SELECT killmail_hash FROM zkill_killmails WHERE killmail_id = 138772244;"));
        Assert.Equal(new[] { 0L, 0L, 1L }, new[]
        {
            (long)Scalar(target, "SELECT is_solo FROM zkill_killmails WHERE killmail_id = 138772243;")!,
            (long)Scalar(target, "SELECT is_npc FROM zkill_killmails WHERE killmail_id = 138772243;")!,
            (long)Scalar(target, "SELECT is_qualifying FROM zkill_killmails WHERE killmail_id = 138772243;")!
        });
        Assert.Null(Scalar(target, "SELECT victim_character_id FROM zkill_killmails WHERE killmail_id = 138772244;"));

        Assert.Equal(Unix(2026, 9, 28, 18, 39, 3), Scalar(target, "SELECT last_active_utc FROM zkill_activity_cache WHERE character_id = 2116955190;"));
        Assert.Equal(1L, Scalar(target, "SELECT has_public_activity_data FROM zkill_activity_cache WHERE character_id = 2116955190;"));
        Assert.Equal(0L, Scalar(target, "SELECT has_public_activity_data FROM zkill_activity_cache WHERE character_id = 2112625428;"));
        Assert.Null(Scalar(target, "SELECT last_active_utc FROM zkill_activity_cache WHERE character_id = 2112625428;"));
        Assert.Equal("timeout", Scalar(target, "SELECT error FROM zkill_activity_cache WHERE character_id = 2112625428;"));

        Assert.Equal("T'ral Vsengne", Scalar(target, "SELECT input_name FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal("Corp O'Neil", Scalar(target, "SELECT corporation_name FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal(-4.5, Scalar(target, "SELECT security_status FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal(Unix(2026, 9, 28, 18, 39, 3), Scalar(target, "SELECT cached_at_utc FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal(Unix(2008, 3, 14, 0, 0, 0), Scalar(target, "SELECT birthday FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal(500001L, Scalar(target, "SELECT faction_id FROM pilot_identity_cache WHERE character_id = 2112625428;"));
        Assert.Equal("Zoë Müller 漢字", Scalar(target, "SELECT input_name FROM pilot_identity_cache WHERE character_id IS NULL;"));
        Assert.Null(Scalar(target, "SELECT birthday FROM pilot_identity_cache WHERE character_id IS NULL;"));

        Assert.Equal("Corp O'Neil Ünïcode", Scalar(target, "SELECT name FROM esi_entity_name_cache WHERE entity_id = 98000001;"));
        Assert.Equal("Alliance 漢字", Scalar(target, "SELECT name FROM esi_entity_name_cache WHERE entity_id = 99000001;"));

        Assert.Equal(0.25, Scalar(target, "SELECT solo_ratio FROM zkill_statistics_cache WHERE character_id = 2116955190;"));
        Assert.Equal(1L, Scalar(target, "SELECT months_processed FROM zkill_statistics_cache WHERE character_id = 2116955190;"));
        Assert.Null(Scalar(target, "SELECT no_history_marker FROM zkill_statistics_cache WHERE character_id = 2116955190;"));
        Assert.Equal(1L, Scalar(target, "SELECT no_history_marker FROM zkill_statistics_cache WHERE character_id = 2112625428;"));

        Assert.Equal(Unix(2026, 9, 28, 18, 39, 3), Scalar(target, "SELECT kill_time_utc FROM pilot_last_killmail_cache WHERE character_id = 2116955190;"));
        Assert.Equal(1L, Scalar(target, "SELECT has_killmail FROM pilot_last_killmail_cache WHERE character_id = 2116955190;"));
        Assert.Null(Scalar(target, "SELECT kill_time_utc FROM pilot_last_killmail_cache WHERE character_id = 2112625428;"));
    }

    [Fact]
    public void Run_Fixture_CopiesMetadataAndKeepsSchemaVersionFour()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"), alphaLock: true, threshold: 5);
        var target = directory.File("target.db");

        var (exitCode, _) = Run(source, target);

        Assert.Equal(0, exitCode);
        Assert.Equal(5L, Scalar(target, "SELECT last_qualification_fleet_threshold FROM schema_metadata;"));
        Assert.Equal(1L, Scalar(target, "SELECT alpha_lock FROM schema_metadata;"));
        Assert.Equal(4L, Scalar(target, "SELECT schema_version FROM schema_metadata;"));
    }

    [Fact]
    public void Run_UnlockedSourceWithoutThreshold_CopiesNullAndUnlocked()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"), alphaLock: false, threshold: null);
        var target = directory.File("target.db");

        var (exitCode, _) = Run(source, target);

        Assert.Equal(0, exitCode);
        Assert.Null(Scalar(target, "SELECT last_qualification_fleet_threshold FROM schema_metadata;"));
        Assert.Equal(0L, Scalar(target, "SELECT alpha_lock FROM schema_metadata;"));
    }

    [Fact]
    public void Run_OrphanAttackerRow_IsSkippedAndReported()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"));
        var target = directory.File("target.db");

        var (exitCode, output) = Run(source, target);

        Assert.Equal(0, exitCode);
        Assert.Equal(3L, Scalar(target, "SELECT COUNT(*) FROM zkill_killmail_attackers;"));
        Assert.Equal(0L, Scalar(target, "SELECT COUNT(*) FROM zkill_killmail_attackers WHERE killmail_id = 999999999;"));

        var attackerLine = output.Split(Environment.NewLine).Single(line => line.StartsWith("zkill_killmail_attackers"));
        var columns = attackerLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "4", "3", "1", "yes" }, columns[1..]);
    }

    [Fact]
    public void Run_Success_LeavesTheSourceFileUnchanged()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"));
        var before = Hash(source);

        var (exitCode, _) = Run(source, directory.File("target.db"));

        Assert.Equal(0, exitCode);
        Assert.Equal(before, Hash(source));
    }

    [Fact]
    public void Run_Result_OpensThroughTheApplicationDatabaseAndSchemaGate()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"));
        var target = directory.File("target.db");

        Assert.Equal(0, Run(source, target).ExitCode);

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = target });

        try
        {
            database.EnsureCreated();
            database.Open();

            var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

            Assert.Equal(SchemaVersionCheckOutcome.Ok, result.Outcome);
            Assert.True(database.GetAlphaLock());
            Assert.Equal(5, database.GetLastAppliedQualificationFleetThreshold());
            Assert.Equal(4, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Run_SourceMissing_RefusesAndWritesNothing()
    {
        using var directory = new TestDirectory();
        var target = directory.File("target.db");

        var (exitCode, output) = Run(directory.File("missing.duckdb"), target);

        Assert.Equal(1, exitCode);
        Assert.Contains("Source database not found", output);
        Assert.Empty(Directory.GetFiles(directory.Path, "target.db*"));
    }

    [Fact]
    public void Run_SourceSchemaVersionNotThree_RefusesAndWritesNothing()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"), schemaVersion: 2);
        var target = directory.File("target.db");

        var (exitCode, output) = Run(source, target);

        Assert.Equal(1, exitCode);
        Assert.Contains("Source schema_version is 2; 3 is required.", output);
        Assert.Empty(Directory.GetFiles(directory.Path, "target.db*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    public void Run_TargetFileAlreadyPresent_RefusesAndLeavesItUntouched(string suffix)
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"));
        var target = directory.File("target.db");
        var existing = target + suffix;

        File.WriteAllText(existing, "existing");

        var (exitCode, output) = Run(source, target);

        Assert.Equal(1, exitCode);
        Assert.Contains("Target already exists", output);
        Assert.Equal("existing", File.ReadAllText(existing));
        Assert.Single(Directory.GetFiles(directory.Path, "target.db*"));
    }

    [Fact]
    public void Run_FailureMidCopy_LeavesNoTargetFilesAndTheSourceUnchanged()
    {
        using var directory = new TestDirectory();
        var source = SourceFixture.Create(directory.File("source.duckdb"), corruptLastKillmailTime: true);
        var before = Hash(source);
        var target = directory.File("target.db");

        var (exitCode, output) = Run(source, target);

        Assert.Equal(1, exitCode);
        Assert.Contains("Migration failed", output);
        Assert.Empty(Directory.GetFiles(directory.Path, "target.db*"));
        Assert.Equal(before, Hash(source));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
