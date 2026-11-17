using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class StorageLifecycleIntegrationTests : IDisposable
{
    private const int FleetThreshold = 11;
    private static readonly TimeSpan ShutdownCheckpointTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"storageLifecycle.{Guid.NewGuid():N}");
    private readonly List<string> _records = [];
    private DateTimeOffset _clock = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Lifecycle_ChangeToNonKillmailTable_ShutdownBackupRestoresItAfterCorruption()
    {
        var first = Startup(isReleaseBuild: true);
        Assert.Equal(StartupOutcome.Ready, first.Result.Outcome);
        Execute(first.Database, "CREATE TABLE main.bulk (id INTEGER PRIMARY KEY, payload TEXT NOT NULL); "
            + "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2000) "
            + "INSERT INTO main.bulk SELECT i, hex(randomblob(64)) FROM n;");
        InsertName(first.Database, 1001, "First Name");
        await Shutdown(first);
        Assert.Equal(1, BackupCount());

        var second = Startup(isReleaseBuild: true);
        Assert.Equal(StartupOutcome.Ready, second.Result.Outcome);
        InsertName(second.Database, 1002, "Second Name");
        await Shutdown(second);
        Assert.Equal(2, BackupCount());

        DamageLastPage(DatabasePath);

        var third = Startup(isReleaseBuild: true);
        await Shutdown(third);

        Assert.Equal(StartupOutcome.Ready, third.Result.Outcome);
        Assert.Contains(_records, record => record.Contains("latest backup", StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(_root, "KillRight.db.corrupt-*"));
        Assert.Equal(2L, CountRows("esi_entity_name_cache"));
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, ReadSchemaVersion());
        Assert.True(ReadAlphaLock());
    }

    [Fact]
    public async Task Lifecycle_ShutdownWithoutChange_WritesNoSecondBackup()
    {
        var first = Startup(isReleaseBuild: false);
        InsertName(first.Database, 1001, "Name");
        await Shutdown(first);

        var second = Startup(isReleaseBuild: false);
        await Shutdown(second);

        Assert.Equal(1, BackupCount());
    }

    [Fact]
    public async Task Lifecycle_LockedNewerDatabase_IsRefusedAndLeftUntouched()
    {
        var first = Startup(isReleaseBuild: true);
        InsertKillmail(first.Database, 810001);
        first.Database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion + 1);
        await Shutdown(first);

        var second = Startup(isReleaseBuild: true);
        var secondOutcome = second.Result;
        await Shutdown(second);

        Assert.Equal(StartupOutcome.SchemaRefused, secondOutcome.Outcome);
        Assert.Equal(SchemaVersionCheckOutcome.RefusedDowngrade, secondOutcome.Schema?.Outcome);
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion + 1, ReadSchemaVersion());
        Assert.Equal(1L, CountRows("zkill_killmails"));
        Assert.True(ReadAlphaLock());
    }

    [Fact]
    public async Task Lifecycle_LockedOlderDatabaseWithoutMigrationPath_IsRefusedAndKeepsKillmails()
    {
        var first = Startup(isReleaseBuild: true);
        InsertKillmail(first.Database, 810001);
        first.Database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);
        await Shutdown(first);

        var second = Startup(isReleaseBuild: true);
        var secondOutcome = second.Result;
        await Shutdown(second);

        Assert.Equal(StartupOutcome.SchemaRefused, secondOutcome.Outcome);
        Assert.Equal(SchemaVersionCheckOutcome.RefusedNoMigrationPath, secondOutcome.Schema?.Outcome);
        Assert.Equal(1L, CountRows("zkill_killmails"));
    }

    [Fact]
    public async Task Lifecycle_UnlockedOlderDatabase_IsRebuiltKeepsOtherTablesThenLocks()
    {
        var first = Startup(isReleaseBuild: false);
        InsertKillmail(first.Database, 810001);
        InsertName(first.Database, 1001, "Kept Name");
        first.Database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);
        await Shutdown(first);
        Assert.False(ReadAlphaLock());

        var second = Startup(isReleaseBuild: true);
        await Shutdown(second);

        Assert.Equal(StartupOutcome.Ready, second.Result.Outcome);
        Assert.Equal(0L, CountRows("zkill_killmails"));
        Assert.Equal(1L, CountRows("esi_entity_name_cache"));
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, ReadSchemaVersion());
        Assert.True(ReadAlphaLock());

        var third = Startup(isReleaseBuild: true);
        InsertKillmail(third.Database, 810002);
        third.Database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);
        await Shutdown(third);

        var fourth = Startup(isReleaseBuild: true);
        await Shutdown(fourth);

        Assert.Equal(StartupOutcome.SchemaRefused, fourth.Result.Outcome);
        Assert.Equal(1L, CountRows("zkill_killmails"));
    }

    private string DatabasePath => Path.Combine(_root, "KillRight.db");

    private string BackupFolder => Path.Combine(_root, "Backups");

    private (KillRightDatabase Database, DatabaseBackupService Service, StartupResult Result) Startup(bool isReleaseBuild)
    {
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = DatabasePath });
        var service = new DatabaseBackupService(database, BackupFolder, 5, () => _clock = _clock.AddMinutes(1));
        var result = GuardedStartup.Run(database, service, isReleaseBuild, FleetThreshold, _records.Add);

        return (database, service, result);
    }

    private static async Task Shutdown((KillRightDatabase Database, DatabaseBackupService Service, StartupResult Result) instance)
    {
        if (instance.Database.IsOpen)
        {
            await instance.Service.BackupAsync();
            Assert.True(instance.Database.Checkpoint(ShutdownCheckpointTimeout));
        }

        instance.Database.Close();
    }

    private int BackupCount()
    {
        return Directory.Exists(BackupFolder) ? Directory.GetFiles(BackupFolder, "KillRight-*.db").Length : 0;
    }

    private static void InsertName(KillRightDatabase database, long id, string name)
    {
        Execute(database, $"INSERT INTO main.esi_entity_name_cache (entity_id, entity_type, name) VALUES ({id}, 'character', '{name}');");
    }

    private static void InsertKillmail(KillRightDatabase database, long killmailId)
    {
        Execute(database, $"""
            INSERT INTO main.zkill_killmails (
                killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id,
                victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc
            ) VALUES (
                {killmailId}, 'hash{killmailId}', 1790553600, 30000142, 40000001, NULL, 587, 2, 0, 0, 1, 1790553600
            );
            """);
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DamageLastPage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Seek(-4096, SeekOrigin.End);
        stream.Write(Enumerable.Repeat((byte)0xFF, 4096).ToArray());
    }

    private long CountRows(string table)
    {
        return Convert.ToInt64(ReadScalar($"SELECT COUNT(*) FROM main.{table};"));
    }

    private int ReadSchemaVersion()
    {
        return Convert.ToInt32(ReadScalar("SELECT schema_version FROM main.schema_metadata;"));
    }

    private bool ReadAlphaLock()
    {
        return Convert.ToInt32(ReadScalar("SELECT alpha_lock FROM main.schema_metadata;")) == 1;
    }

    private object? ReadScalar(string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }
}
