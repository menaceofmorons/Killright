using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DatabaseBackupServiceTests
{
    private static readonly string[] AllTables =
    [
        "pilot_identity_cache", "esi_entity_name_cache", "zkill_activity_cache", "zkill_statistics_cache",
        "pilot_last_killmail_cache", "zkill_killmails", "zkill_killmail_attackers", "sde_types",
        "sde_solar_systems", "sde_npc_corporations", "sde_factions", "sde_metadata", "schema_metadata"
    ];

    [Fact]
    public async Task BackupAsync_NoPriorBackup_CreatesOneDatabaseCopyWithTheSameRowCountInEveryTable()
    {
        var fixture = CreateFixture(rotationCount: 5);

        InsertKillmail(fixture.Database, 800001);
        InsertAttacker(fixture.Database, 800001, 95465499);
        Execute(fixture.Database, "INSERT INTO main.sde_types (type_id, name) VALUES (587, 'Rifter'), (11567, 'Crow');");
        Execute(fixture.Database, "INSERT INTO main.esi_entity_name_cache (entity_id, entity_type, name) VALUES (98000001, 'corporation', 'Corp One');");

        await fixture.Service.BackupAsync();

        var backup = Assert.Single(BackupFiles(fixture.Folder));
        Assert.Single(Directory.GetFiles(fixture.Folder, "*.fingerprint"));
        Assert.Empty(Directory.GetFiles(fixture.Folder, "*.tmp"));

        foreach (var table in AllTables)
            Assert.Equal(CountRows(fixture.Database.DatabasePath, table), CountRows(backup, table));
    }

    [Fact]
    public async Task BackupAsync_CopyOpensReadOnlyAndPassesQuickCheck()
    {
        var fixture = CreateFixture(rotationCount: 5);
        InsertKillmail(fixture.Database, 800002);

        await fixture.Service.BackupAsync();

        var backup = Assert.Single(BackupFiles(fixture.Folder));

        Assert.Equal("ok", QuickCheck(backup));
    }

    [Fact]
    public async Task BackupAsync_FailureAfterTheCopy_RemovesTheTemporaryFileAndThePreviousCopyStands()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 5, clock);
        InsertKillmail(fixture.Database, 800003);
        await fixture.Service.BackupAsync();
        var previous = Assert.Single(BackupFiles(fixture.Folder));

        InsertKillmail(fixture.Database, 800004);
        clock.Advance(TimeSpan.FromSeconds(5));
        Directory.CreateDirectory(Path.Combine(fixture.Folder, $"KillRight-{clock.UtcNow:yyyyMMdd'T'HHmmss'Z'}.db"));

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.BackupAsync());

        Assert.Empty(Directory.GetFiles(fixture.Folder, "*.tmp"));
        Assert.True(File.Exists(previous));
        Assert.Equal(1L, CountRows(previous, "zkill_killmails"));
    }

    [Fact]
    public async Task BackupAsync_MoreCopiesThanRotationCount_KeepsTheNewestAndTheirFingerprints()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 2, clock);

        for (var index = 0; index < 4; index++)
        {
            InsertKillmail(fixture.Database, 800010 + index);
            await fixture.Service.BackupAsync();
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        var backups = BackupFiles(fixture.Folder);

        Assert.Equal(2, backups.Length);
        Assert.Equal(2, Directory.GetFiles(fixture.Folder, "*.fingerprint").Length);
        Assert.Equal(4L, CountRows(backups[0], "zkill_killmails"));
        Assert.Equal(3L, CountRows(backups[1], "zkill_killmails"));
    }

    [Fact]
    public async Task BackupAsync_DatabaseUnchangedSinceLastBackup_DoesNotCreateANewCopy()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 5, clock);
        InsertKillmail(fixture.Database, 800020);

        await fixture.Service.BackupAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.Service.BackupAsync();

        Assert.Single(BackupFiles(fixture.Folder));
    }

    [Fact]
    public async Task BackupAsync_DatabaseChangedSinceLastBackup_CreatesANewCopy()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 5, clock);
        InsertKillmail(fixture.Database, 800021);
        await fixture.Service.BackupAsync();

        InsertKillmail(fixture.Database, 800022);
        clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.Service.BackupAsync();

        Assert.Equal(2, BackupFiles(fixture.Folder).Length);
    }

    [Fact]
    public async Task BackupAsync_OldParquetFolders_AreIgnoredAndNeverDeleted()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 1, clock);
        var legacy = Path.Combine(fixture.Folder, "20260101T000000Z");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "zkill_killmails.parquet"), "legacy");

        for (var index = 0; index < 3; index++)
        {
            InsertKillmail(fixture.Database, 800030 + index);
            await fixture.Service.BackupAsync();
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.True(File.Exists(Path.Combine(legacy, "zkill_killmails.parquet")));
        Assert.Single(BackupFiles(fixture.Folder));
    }

    [Fact]
    public void TryRestore_OnlyOldParquetFoldersExist_ReturnsFalse()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"databaseBackupFolder.{Guid.NewGuid():N}");
        var legacy = Path.Combine(folder, "20260101T000000Z");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "zkill_killmails.parquet"), "legacy");
        var restoreDatabase = NewDatabase();

        Assert.False(new DatabaseBackupService(restoreDatabase, folder, 5).TryRestore());
        Assert.False(File.Exists(restoreDatabase.DatabasePath));
    }

    [Fact]
    public void TryRestore_NoBackupExists_ReturnsFalseAndCreatesNoFile()
    {
        var fixture = CreateFixture(rotationCount: 5);
        var restoreDatabase = NewDatabase();
        var service = new DatabaseBackupService(restoreDatabase, fixture.Folder, 5);

        Assert.False(service.TryRestore());
        Assert.False(File.Exists(restoreDatabase.DatabasePath));
    }

    [Fact]
    public async Task TryRestore_BackupExists_CopiesTheNewestPassingBackupAndTheRestoredFileOpens()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 5, clock);
        InsertKillmail(fixture.Database, 800040);
        InsertAttacker(fixture.Database, 800040, 95465499);
        await fixture.Service.BackupAsync();

        var restoreDatabase = NewDatabase();
        var restored = new DatabaseBackupService(restoreDatabase, fixture.Folder, 5).TryRestore();

        Assert.True(restored);

        restoreDatabase.EnsureCreated();
        restoreDatabase.Open();

        try
        {
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, restoreDatabase.GetSchemaVersion());
            Assert.Equal(1L, CountRows(restoreDatabase.DatabasePath, "zkill_killmails"));
            Assert.Equal(1L, CountRows(restoreDatabase.DatabasePath, "zkill_killmail_attackers"));
        }
        finally
        {
            restoreDatabase.Close();
        }
    }

    [Fact]
    public async Task TryRestore_NewestCopyIsCorrupt_SkipsItAndRestoresTheNextNewest()
    {
        var clock = new FakeClock();
        var fixture = CreateFixture(rotationCount: 5, clock);
        InsertKillmail(fixture.Database, 800050);
        await fixture.Service.BackupAsync();

        InsertKillmail(fixture.Database, 800051);
        clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.Service.BackupAsync();

        var newest = BackupFiles(fixture.Folder)[0];
        File.WriteAllBytes(newest, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

        var restoreDatabase = NewDatabase();

        Assert.True(new DatabaseBackupService(restoreDatabase, fixture.Folder, 5).TryRestore());
        Assert.Equal(1L, CountRows(restoreDatabase.DatabasePath, "zkill_killmails"));
    }

    [Fact]
    public async Task StartupSequence_DatabaseFileDeleted_IsRestoredFromTheLatestBackup()
    {
        var fixture = CreateFixture(rotationCount: 5);
        InsertKillmail(fixture.Database, 800070);
        await fixture.Service.BackupAsync();
        fixture.Database.Close();
        File.Delete(fixture.Database.DatabasePath);

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = fixture.Database.DatabasePath });
        var service = new DatabaseBackupService(database, fixture.Folder, 5);

        Assert.False(database.QuarantineIfCorrupt());
        Assert.True(!File.Exists(database.DatabasePath) && service.TryRestore());

        database.EnsureCreated();
        database.Open();

        try
        {
            Assert.Equal(1L, CountRows(database.DatabasePath, "zkill_killmails"));
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task StartupSequence_DatabaseFirstBytesOverwritten_IsQuarantinedThenRestored()
    {
        var fixture = CreateFixture(rotationCount: 5);
        InsertKillmail(fixture.Database, 800071);
        await fixture.Service.BackupAsync();
        fixture.Database.Close();

        using (var stream = new FileStream(fixture.Database.DatabasePath, FileMode.Open, FileAccess.Write))
            stream.Write(Enumerable.Repeat((byte)0x5A, 512).ToArray());

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = fixture.Database.DatabasePath });
        var service = new DatabaseBackupService(database, fixture.Folder, 5);

        Assert.True(database.QuarantineIfCorrupt());
        Assert.True(service.TryRestore());

        database.EnsureCreated();
        database.Open();

        try
        {
            Assert.Equal(1L, CountRows(database.DatabasePath, "zkill_killmails"));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(database.DatabasePath)!, $"{Path.GetFileName(database.DatabasePath)}.corrupt-*"));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task TryRestore_EveryCopyIsCorrupt_ReturnsFalseAndCreatesNoFile()
    {
        var fixture = CreateFixture(rotationCount: 5);
        InsertKillmail(fixture.Database, 800060);
        await fixture.Service.BackupAsync();

        foreach (var backup in BackupFiles(fixture.Folder))
            File.WriteAllBytes(backup, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

        var restoreDatabase = NewDatabase();

        Assert.False(new DatabaseBackupService(restoreDatabase, fixture.Folder, 5).TryRestore());
        Assert.False(File.Exists(restoreDatabase.DatabasePath));
    }

    private static Fixture CreateFixture(int rotationCount, FakeClock? clock = null)
    {
        var database = NewDatabase();
        database.EnsureCreated();

        var folder = Path.Combine(Path.GetTempPath(), $"databaseBackupFolder.{Guid.NewGuid():N}");
        var service = new DatabaseBackupService(database, folder, rotationCount, clock is null ? null : () => clock.UtcNow);

        return new Fixture(database, folder, service);
    }

    private static KillRightDatabase NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"databaseBackup.{Guid.NewGuid():N}.db");

        return new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
    }

    private static string[] BackupFiles(string folder)
    {
        return Directory.GetFiles(folder, "KillRight-*.db")
            .OrderByDescending(Path.GetFileName)
            .ToArray();
    }

    private static long CountRows(string databasePath, string table)
    {
        using var connection = OpenReadOnly(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{table};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? QuickCheck(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";

        return Convert.ToString(command.ExecuteScalar());
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();

        return connection;
    }

    private static void InsertKillmail(KillRightDatabase database, long killmailId)
    {
        Execute(database, $"""
            INSERT INTO main.zkill_killmails (
                killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id,
                victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc
            ) VALUES (
                {killmailId}, 'hash{killmailId}', 1790553600, 30000142, 40000001, NULL, 587, 2, 0, 0, 1, {1790553600 + killmailId % 1000}
            );
            """);
    }

    private static void InsertAttacker(KillRightDatabase database, long killmailId, long characterId)
    {
        Execute(database, $"""
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id)
            VALUES ({killmailId}, {characterId}, 98000001, NULL, 11567);
            """);
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record Fixture(KillRightDatabase Database, string Folder, DatabaseBackupService Service);

    private sealed class FakeClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan span)
        {
            UtcNow += span;
        }
    }
}
