using Microsoft.Data.Sqlite;
using Killright.Storage.Database;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class SchemaVersionGateTests
{
    [Fact]
    public void CheckOnStartup_NewDatabase_ReturnsOkWithoutRebuilding()
    {
        var database = CreateDatabase();

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.Equal(SchemaVersionCheckOutcome.Ok, result.Outcome);
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void CheckOnStartup_AlphaReleaseAndLockAbsent_SetsLock()
    {
        var database = CreateDatabase();

        SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: true);

        Assert.True(database.GetAlphaLock());
    }

    [Fact]
    public void CheckOnStartup_NotAlphaRelease_LeavesLockUnset()
    {
        var database = CreateDatabase();

        SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.False(database.GetAlphaLock());
    }

    [Fact]
    public void CheckOnStartup_NotLockedVersionMismatch_RebuildsKillmailAndAttackerTablesAndStoresCurrentVersion()
    {
        var database = CreateDatabase();
        InsertKillmailRow(database, 900001);
        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.Equal(SchemaVersionCheckOutcome.Ok, result.Outcome);
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        Assert.Equal(0, CountKillmails(database));
    }

    [Fact]
    public void CheckOnStartup_LockedAndVersionMatches_ReturnsOkWithoutRebuilding()
    {
        var database = CreateDatabase();
        InsertKillmailRow(database, 900002);
        SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: true);

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: true);

        Assert.Equal(SchemaVersionCheckOutcome.Ok, result.Outcome);
        Assert.Equal(1, CountKillmails(database));
    }

    [Fact]
    public void CheckOnStartup_LockedAndStoredVersionNewer_RefusesAsDowngrade()
    {
        var database = CreateDatabase();
        database.SetAlphaLock();
        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion + 1);

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.Equal(SchemaVersionCheckOutcome.RefusedDowngrade, result.Outcome);
    }

    [Fact]
    public void CheckOnStartup_LockedAndStoredVersionOlderWithNoMigrationPath_RefusesWithNoMigrationPath()
    {
        var database = CreateDatabase();
        database.SetAlphaLock();
        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 2);

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.Equal(SchemaVersionCheckOutcome.RefusedNoMigrationPath, result.Outcome);
    }

    [Fact]
    public void CheckOnStartup_LockedAndStoredVersionOneBehind_RefusesWithNoMigrationPathAndKeepsKillmails()
    {
        var database = CreateDatabase();
        InsertKillmailRow(database, 900003);
        database.SetAlphaLock();
        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);

        var result = SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        Assert.Equal(SchemaVersionCheckOutcome.RefusedNoMigrationPath, result.Outcome);
        Assert.Equal(1, CountKillmails(database));
    }

    [Fact]
    public void CheckOnStartup_NotLockedRebuild_RecreatesKillmailTablesAsStrictWithCascade()
    {
        var database = CreateDatabase();
        InsertKillmailRow(database, 900004);
        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion - 1);

        SchemaVersionGate.CheckOnStartup(database, isAlphaRelease: false);

        using var connection = database.OpenConnection();

        Assert.Equal(2L, ScalarLong(connection, "SELECT COUNT(*) FROM pragma_table_list WHERE schema = 'main' AND name IN ('zkill_killmails', 'zkill_killmail_attackers') AND strict = 1;"));
        Assert.Equal(1L, ScalarLong(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('zkill_killmail_attackers') WHERE on_delete = 'CASCADE';"));
        Assert.Equal(3L, ScalarLong(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name LIKE 'ix_zkill_%';"));
    }

    [Fact]
    public void Migrations_AreEmpty()
    {
        Assert.Empty(SchemaVersionGate.Migrations);
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schemaVersionGate.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return database;
    }

    private static void InsertKillmailRow(KillRightDatabase database, long killmailId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_killmails
                (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id,
                 victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc)
            VALUES
                ({killmailId}, 'hash', 1790553600, 30000142, NULL, 95465499,
                 587, 1, 1, 0, 0, 1790553600);
            """;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static int CountKillmails(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM main.zkill_killmails;";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}
