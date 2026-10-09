using Killright.Storage.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class KillRightDatabaseSchemaVersionTests
{
    [Fact]
    public void EnsureCreated_NewDatabase_StoresCurrentSchemaVersion()
    {
        var database = CreateDatabase();

        database.EnsureCreated();

        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotDuplicateSchemaMetadataRow()
    {
        var database = CreateDatabase();

        database.EnsureCreated();
        database.EnsureCreated();

        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void CurrentSchemaVersion_IsFour()
    {
        Assert.Equal(4, KillRightDatabase.CurrentSchemaVersion);
    }

    [Fact]
    public void EnsureCreated_EveryTable_IsStrict()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        var tables = ScalarLong(connection, "SELECT COUNT(*) FROM pragma_table_list WHERE schema = 'main' AND name NOT LIKE 'sqlite_%';");
        var strictTables = ScalarLong(connection, "SELECT COUNT(*) FROM pragma_table_list WHERE schema = 'main' AND name NOT LIKE 'sqlite_%' AND strict = 1;");

        Assert.Equal(13L, tables);
        Assert.Equal(tables, strictTables);
    }

    [Fact]
    public void EnsureCreated_TextIntoIntegerColumn_Fails()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO main.esi_entity_name_cache (entity_id, entity_type, name) VALUES ('not a number', 'character', 'Lukas Naarii');"));
    }

    [Fact]
    public void EnsureCreated_FlagOfTwo_Fails()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO main.pilot_last_killmail_cache (character_id, has_killmail, checked_at_utc) VALUES (95465499, 2, 1790553600);"));
    }

    [Fact]
    public void EnsureCreated_DeletingAKillmail_DeletesItsAttackerRows()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        Execute(connection, """
            INSERT INTO main.zkill_killmails
                (killmail_id, kill_time_utc, system_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc)
            VALUES (1, 1790553600, 30000142, 2, 0, 0, 1, 1790553600), (2, 1790553600, 30000142, 1, 1, 0, 0, 1790553600);
            """);
        Execute(connection, "INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id) VALUES (1, 100), (1, 101), (2, 100);");

        Execute(connection, "DELETE FROM main.zkill_killmails WHERE killmail_id = 1;");

        Assert.Equal(1L, ScalarLong(connection, "SELECT COUNT(*) FROM main.zkill_killmail_attackers;"));
        Assert.Equal(2L, ScalarLong(connection, "SELECT killmail_id FROM main.zkill_killmail_attackers;"));
    }

    [Fact]
    public void EnsureCreated_AttackerForUnknownKillmail_Fails()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id) VALUES (999, 100);"));
    }

    [Fact]
    public void EnsureCreated_NewDatabase_AlphaLockDefaultsFalse()
    {
        var database = CreateDatabase();

        database.EnsureCreated();

        Assert.False(database.GetAlphaLock());
    }

    [Fact]
    public void SetAlphaLock_NewDatabase_PersistsLock()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        database.SetAlphaLock();

        Assert.True(database.GetAlphaLock());
    }

    [Fact]
    public void SetSchemaVersion_NewDatabase_PersistsVersion()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        database.SetSchemaVersion(KillRightDatabase.CurrentSchemaVersion + 1);

        Assert.Equal(KillRightDatabase.CurrentSchemaVersion + 1, database.GetSchemaVersion());
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schemaVersion.{Guid.NewGuid():N}.db");
        return new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
    }
}
