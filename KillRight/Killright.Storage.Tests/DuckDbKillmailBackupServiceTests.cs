using DuckDB.NET.Data;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbKillmailBackupServiceTests
{
    [Fact]
    public async Task BackupAsync_NoPriorBackup_CreatesOneTimestampedFolderWithBothTables()
    {
        var (database, backupFolder, service) = CreateService(rotationCount: 5);

        InsertKillmail(database, 800001, isQualifying: true);
        InsertAttacker(database, 800001, 95465499);

        await service.BackupAsync();

        var folders = Directory.GetDirectories(backupFolder);
        Assert.Single(folders);
        Assert.True(File.Exists(Path.Combine(folders[0], "zkill_killmails.parquet")));
        Assert.True(File.Exists(Path.Combine(folders[0], "zkill_killmail_attackers.parquet")));
    }

    [Fact]
    public async Task BackupAsync_MoreBackupsThanRotationCount_DeletesOldestBeyondRotationCount()
    {
        var (database, backupFolder, service) = CreateService(rotationCount: 2);

        InsertKillmail(database, 800002, isQualifying: true);
        await service.BackupAsync();

        await Task.Delay(1100);
        InsertKillmail(database, 800005, isQualifying: true);
        await service.BackupAsync();

        await Task.Delay(1100);
        InsertKillmail(database, 800006, isQualifying: true);
        await service.BackupAsync();

        var folders = Directory.GetDirectories(backupFolder);
        Assert.Equal(2, folders.Length);
    }

    [Fact]
    public async Task BackupAsync_DatabaseUnchangedSinceLastBackup_DoesNotCreateNewFolder()
    {
        var (database, backupFolder, service) = CreateService(rotationCount: 5);

        InsertKillmail(database, 800007, isQualifying: true);

        await service.BackupAsync();
        await service.BackupAsync();

        var folders = Directory.GetDirectories(backupFolder);
        Assert.Single(folders);
    }

    [Fact]
    public async Task TryRestoreAsync_NoBackupExists_ReturnsFalse()
    {
        var (_, _, service) = CreateService(rotationCount: 5);

        var restored = await service.TryRestoreAsync();

        Assert.False(restored);
    }

    [Fact]
    public async Task TryRestoreAsync_BackupExists_RepopulatesEmptyTablesFromLatestBackup()
    {
        var (sourceDatabase, backupFolder, sourceService) = CreateService(rotationCount: 5);

        InsertKillmail(sourceDatabase, 800003, isQualifying: true);
        InsertAttacker(sourceDatabase, 800003, 95465499);

        await sourceService.BackupAsync();

        var restoredDatabasePath = Path.Combine(Path.GetTempPath(), $"killmailBackupRestore.{Guid.NewGuid():N}.duckdb");
        var restoredDatabase = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = restoredDatabasePath });
        restoredDatabase.EnsureCreated();
        var restoreService = new DuckDbKillmailBackupService(restoredDatabase, backupFolder, 5);

        var restored = await restoreService.TryRestoreAsync();

        Assert.True(restored);
        Assert.True(KillmailExists(restoredDatabase, 800003));
        Assert.Equal(1, CountAttackers(restoredDatabase, 800003));
    }

    private static (KillRightDatabase Database, string BackupFolder, DuckDbKillmailBackupService Service) CreateService(int rotationCount)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"killmailBackup.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = databasePath });
        database.EnsureCreated();

        var backupFolder = Path.Combine(Path.GetTempPath(), $"killmailBackupFolder.{Guid.NewGuid():N}");

        return (database, backupFolder, new DuckDbKillmailBackupService(database, backupFolder, rotationCount));
    }

    private static void InsertKillmail(KillRightDatabase database, long killmailId, bool isQualifying)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
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
                                  {killmailId},
                                  'hash{killmailId}',
                                  '{DateTimeOffset.UtcNow.UtcDateTime:O}',
                                  30000142,
                                  40000001,
                                  NULL,
                                  587,
                                  2,
                                  FALSE,
                                  FALSE,
                                  {(isQualifying ? "TRUE" : "FALSE")},
                                  '{DateTimeOffset.UtcNow.UtcDateTime:O}'
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertAttacker(KillRightDatabase database, long killmailId, long characterId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              INSERT INTO main.zkill_killmail_attackers (
                                  killmail_id,
                                  character_id,
                                  corporation_id,
                                  alliance_id,
                                  ship_type_id
                              ) VALUES (
                                  {killmailId},
                                  {characterId},
                                  98000001,
                                  NULL,
                                  11567
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static bool KillmailExists(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return Convert.ToInt64(command.ExecuteScalar()!) > 0;
    }

    private static int CountAttackers(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmail_attackers WHERE killmail_id = {killmailId};";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}
