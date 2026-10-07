using DuckDB.NET.Data;
using Killright.Storage.Database;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class KillRightDatabaseInstanceTests
{
    [Fact]
    public void Open_TwoConnections_ShareTheInstanceAndTheConfiguredSettings()
    {
        var database = CreateDatabase(memoryLimit: "512MB", threads: 2);
        database.EnsureCreated();
        database.Open();

        try
        {
            using var first = database.OpenConnection();
            using var second = database.OpenConnection();

            var firstLimit = ReadSetting(first, "memory_limit");

            Assert.Equal(firstLimit, ReadSetting(second, "memory_limit"));
            Assert.NotEqual(PristineMemoryLimit(), firstLimit);
            Assert.Equal("2", ReadSetting(first, "threads"));
            Assert.Equal("2", ReadSetting(second, "threads"));
            Assert.True(database.VerifySharedInstance());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Open_RowCommittedOnOneConnection_IsReadOnTheOther()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using var writer = database.OpenConnection();
            using var reader = database.OpenConnection();

            Execute(writer, "CREATE TABLE main.instance_probe (id BIGINT);");
            Execute(writer, "INSERT INTO main.instance_probe VALUES (42);");

            Assert.Equal(42L, Convert.ToInt64(Scalar(reader, "SELECT id FROM main.instance_probe;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Open_AppliesNormalisedSettingsWhenConfiguredValuesAreInvalid()
    {
        var database = CreateDatabase(memoryLimit: "lots", threads: 0);
        database.EnsureCreated();
        database.Open();

        try
        {
            using var connection = database.OpenConnection();

            Assert.Equal(PristineMemoryLimit(KillRightDatabaseOptions.DefaultMemoryLimit), ReadSetting(connection, "memory_limit"));
            Assert.Equal(KillRightDatabaseOptions.DefaultThreads.ToString(), ReadSetting(connection, "threads"));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Close_ThenOpenConnection_Throws()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        database.Close();

        Assert.False(database.IsOpen);
        Assert.Throws<InvalidOperationException>(() => database.OpenConnection());
    }

    [Fact]
    public void Reopen_AfterClose_RestoresAccessWithTheSameSettings()
    {
        var database = CreateDatabase(memoryLimit: "512MB", threads: 2);
        database.EnsureCreated();
        database.Open();
        var generation = database.InstanceGeneration;

        database.Close();
        database.Reopen();

        try
        {
            using var connection = database.OpenConnection();

            Assert.True(database.IsOpen);
            Assert.Equal(generation + 1, database.InstanceGeneration);
            Assert.NotEqual(PristineMemoryLimit(), ReadSetting(connection, "memory_limit"));
            Assert.Equal("2", ReadSetting(connection, "threads"));
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Checkpoint_LeavesTheWalFileEmptyOrAbsent()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using (var connection = database.OpenConnection())
            {
                Execute(connection, "CREATE TABLE main.checkpoint_probe (id BIGINT);");
                Execute(connection, "INSERT INTO main.checkpoint_probe SELECT range FROM range(1000);");
            }

            database.Checkpoint();

            Assert.False(database.HasPendingWal());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void RecoveryAndSchemaCheckRunBeforeOpen_CorruptFileIsQuarantinedThenInstanceOpens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"instance.{Guid.NewGuid():N}.duckdb");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });

        var wasRecovered = database.EnsureCreatedWithRecovery();
        database.Open();

        try
        {
            Assert.True(wasRecovered);
            Assert.True(database.IsOpen);
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void OpenConnection_BeforeOpen_StillOpensAPlainConnection()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        using var connection = database.OpenConnection();

        Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "SELECT 1;")));
        Assert.False(database.IsOpen);
    }

    [Fact]
    public void Open_CalledTwice_KeepsOneInstance()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        database.Open();
        var generation = database.InstanceGeneration;
        database.Open();

        try
        {
            Assert.Equal(generation, database.InstanceGeneration);
        }
        finally
        {
            database.Close();
        }
    }

    [Theory]
    [InlineData("Invalid Error: database has been invalidated because of a previous fatal error", true)]
    [InlineData("DATABASE HAS BEEN INVALIDATED", true)]
    [InlineData("Catalog Error: table does not exist", false)]
    public void IsInvalidatedFailure_RecognisesTheInvalidatedMessage(string message, bool expected)
    {
        Assert.Equal(expected, KillRightDatabase.IsInvalidatedFailure(new InvalidOperationException(message)));
    }

    [Fact]
    public void IsInvalidatedFailure_ChecksInnerExceptions()
    {
        var wrapped = new InvalidOperationException("outer", new InvalidOperationException("database has been invalidated"));

        Assert.True(KillRightDatabase.IsInvalidatedFailure(wrapped));
        Assert.False(KillRightDatabase.IsInvalidatedFailure(null));
    }

    [Fact]
    public void NotifyFailure_InvalidatedError_ReopensTheInstanceOnce()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();
        var generation = database.InstanceGeneration;

        try
        {
            database.NotifyFailure(new InvalidOperationException("Catalog Error: table does not exist"));
            Assert.Equal(generation, database.InstanceGeneration);

            database.NotifyFailure(new InvalidOperationException("database has been invalidated"));

            Assert.Equal(generation + 1, database.InstanceGeneration);
            Assert.True(database.IsOpen);
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    [Theory]
    [InlineData("1GB", true)]
    [InlineData("512 MB", true)]
    [InlineData("1.5GiB", true)]
    [InlineData("0GB", false)]
    [InlineData("1GB; DROP TABLE x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidMemoryLimit_AcceptsOnlyAmountAndUnit(string? value, bool expected)
    {
        Assert.Equal(expected, KillRightDatabaseOptions.IsValidMemoryLimit(value));
    }

    private static KillRightDatabase CreateDatabase(string? memoryLimit = null, int? threads = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"instance.{Guid.NewGuid():N}.duckdb");

        return new KillRightDatabase(new KillRightDatabaseOptions
        {
            DatabasePath = path,
            MemoryLimit = memoryLimit ?? KillRightDatabaseOptions.DefaultMemoryLimit,
            Threads = threads ?? KillRightDatabaseOptions.DefaultThreads
        });
    }

    private static string? PristineMemoryLimit(string? configured = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"instance.{Guid.NewGuid():N}.duckdb");

        using var connection = new DuckDBConnection($"Data Source={path}");
        connection.Open();

        if (configured is not null)
            Execute(connection, $"SET memory_limit='{configured}';");

        return ReadSetting(connection, "memory_limit");
    }

    private static string? ReadSetting(DuckDBConnection connection, string name)
    {
        return Convert.ToString(Scalar(connection, $"SELECT current_setting('{name}');"));
    }

    private static object? Scalar(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
