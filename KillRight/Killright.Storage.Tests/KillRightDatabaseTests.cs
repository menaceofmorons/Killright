using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class KillRightDatabaseTests
{
    [Fact]
    public void OpenConnection_EveryConnection_HasTheConfiguredPragmas()
    {
        var database = CreateDatabase(busyTimeoutSeconds: 7, pageCacheMegabytes: 16);
        database.EnsureCreated();
        database.Open();

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var connection = database.OpenConnection();

                Assert.Equal("wal", Convert.ToString(Scalar(connection, "PRAGMA journal_mode;")));
                Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "PRAGMA foreign_keys;")));
                Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "PRAGMA synchronous;")));
                Assert.Equal(7000L, Convert.ToInt64(Scalar(connection, "PRAGMA busy_timeout;")));
                Assert.Equal(-16384L, Convert.ToInt64(Scalar(connection, "PRAGMA cache_size;")));
                Assert.Equal(2L, Convert.ToInt64(Scalar(connection, "PRAGMA temp_store;")));
            }
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void OpenConnection_InvalidSettings_UseTheDefaults()
    {
        var database = CreateDatabase(busyTimeoutSeconds: 0, pageCacheMegabytes: -3);
        database.EnsureCreated();
        database.Open();

        try
        {
            using var connection = database.OpenConnection();

            Assert.Equal(KillRightDatabaseOptions.DefaultBusyTimeoutSeconds * 1000L, Convert.ToInt64(Scalar(connection, "PRAGMA busy_timeout;")));
            Assert.Equal(-KillRightDatabaseOptions.DefaultPageCacheMegabytes * 1024L, Convert.ToInt64(Scalar(connection, "PRAGMA cache_size;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void EnsureCreated_NewFile_UsesIncrementalAutoVacuum()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        try
        {
            using var connection = database.OpenConnection();

            Assert.Equal(2L, Convert.ToInt64(Scalar(connection, "PRAGMA auto_vacuum;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task BeginWrite_TwoConcurrentWriters_RunOneAfterTheOtherWithoutBusyErrors()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using (var connection = database.OpenConnection())
                Execute(connection, "CREATE TABLE main.gate_probe (id INTEGER NOT NULL);");

            var active = 0;
            var maximumActive = 0;

            async Task Write(int id)
            {
                await Task.Yield();

                using var scope = database.BeginWrite();

                var now = Interlocked.Increment(ref active);
                InterlockedMax(ref maximumActive, now);

                Execute(scope.Connection, scope.Transaction, $"INSERT INTO main.gate_probe (id) VALUES ({id});");
                await Task.Delay(150);

                Interlocked.Decrement(ref active);
                scope.Commit();
            }

            await Task.WhenAll(Write(1), Write(2), Write(3));

            using var reader = database.OpenConnection();

            Assert.Equal(1, maximumActive);
            Assert.Equal(3L, Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM main.gate_probe;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public async Task BeginWriteAsync_WaitsForTheGate()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            var first = database.BeginWrite();
            var second = database.BeginWriteAsync();

            await Task.Delay(100);
            Assert.False(second.IsCompleted);

            first.Dispose();

            using var scope = await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(scope.Transaction);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void BeginWrite_ReadDuringOpenWriteTransaction_SeesLastCommittedStateWithoutWaiting()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using (var connection = database.OpenConnection())
            {
                Execute(connection, "CREATE TABLE main.read_probe (id INTEGER NOT NULL);");
                Execute(connection, "INSERT INTO main.read_probe (id) VALUES (1);");
            }

            using var scope = database.BeginWrite();
            Execute(scope.Connection, scope.Transaction, "INSERT INTO main.read_probe (id) VALUES (2);");

            using (var reader = database.OpenConnection())
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var count = Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM main.read_probe;"));

                Assert.Equal(1L, count);
                Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1));
            }

            scope.Commit();

            using var after = database.OpenConnection();

            Assert.Equal(2L, Convert.ToInt64(Scalar(after, "SELECT COUNT(*) FROM main.read_probe;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void BeginWrite_DisposeWithoutCommit_RollsBackAndReleasesTheGate()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using (var connection = database.OpenConnection())
                Execute(connection, "CREATE TABLE main.rollback_probe (id INTEGER NOT NULL);");

            using (var scope = database.BeginWrite())
                Execute(scope.Connection, scope.Transaction, "INSERT INTO main.rollback_probe (id) VALUES (1);");

            var second = Task.Run(() =>
            {
                using var scope = database.BeginWrite();
                Execute(scope.Connection, scope.Transaction, "INSERT INTO main.rollback_probe (id) VALUES (2);");
                scope.Commit();
            });

            Assert.True(second.Wait(TimeSpan.FromSeconds(10)));

            using var reader = database.OpenConnection();

            Assert.Equal(1L, Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM main.rollback_probe;")));
            Assert.Equal(2L, Convert.ToInt64(Scalar(reader, "SELECT id FROM main.rollback_probe;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void BeginWrite_WithSessionConnection_WritesOnThatConnection()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using var session = database.OpenScanSession();
            Execute(session.Connection, "CREATE TABLE main.session_probe (id INTEGER NOT NULL);");

            using (var scope = database.BeginWrite(session.Connection))
            {
                Assert.Same(session.Connection, scope.Connection);
                Execute(scope.Connection, scope.Transaction, "INSERT INTO main.session_probe (id) VALUES (5);");
                scope.Commit();
            }

            Assert.Equal(5L, Convert.ToInt64(Scalar(session.Connection, "SELECT id FROM main.session_probe;")));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void BeginWrite_WithTimings_RecordsGateWaitWithTheTag()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();
        var timings = new ScanTimings();

        try
        {
            using (database.BeginWrite(timings: timings, tag: "scan"))
            {
            }

            var row = Assert.Single(timings.Rows, candidate => candidate.Phase == "write_gate_wait");

            Assert.Equal("scan", row.Tag);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Close_ThenOpenConnection_ThrowsAndTheFileCanBeMoved()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        using (var connection = database.OpenConnection())
            Execute(connection, "CREATE TABLE main.close_probe (id INTEGER NOT NULL);");

        database.Close();

        Assert.False(database.IsOpen);
        Assert.Throws<InvalidOperationException>(() => database.OpenConnection());

        var moved = database.DatabasePath + ".moved";
        File.Move(database.DatabasePath, moved);

        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(database.DatabasePath));
    }

    [Fact]
    public void Checkpoint_LeavesTheWalFileEmpty()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();

        try
        {
            using (var scope = database.BeginWrite())
            {
                Execute(scope.Connection, scope.Transaction, "CREATE TABLE main.checkpoint_probe (id INTEGER NOT NULL);");

                for (var id = 0; id < 500; id++)
                    Execute(scope.Connection, scope.Transaction, $"INSERT INTO main.checkpoint_probe (id) VALUES ({id});");

                scope.Commit();
            }

            Assert.True(database.HasPendingWal());

            database.Checkpoint();

            Assert.False(database.HasPendingWal());
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void QuarantineIfCorrupt_CorruptHeader_MovesAllThreeFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quarantine.{Guid.NewGuid():N}.db");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
        File.WriteAllBytes(path + "-wal", [0x01, 0x02]);
        File.WriteAllBytes(path + "-shm", [0x03, 0x04]);
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });

        string? reportedReason = null;
        var quarantined = database.QuarantineIfCorrupt(reason => reportedReason = reason);

        Assert.True(quarantined);
        Assert.NotNull(reportedReason);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-shm"));
        Assert.Single(Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(path)}.corrupt-*"));
        Assert.Single(Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(path)}-wal.corrupt-*"));
        Assert.Single(Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(path)}-shm.corrupt-*"));
    }

    [Fact]
    public void QuarantineIfCorrupt_HealthyFile_IsLeftAlone()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        database.Open();
        database.Close();

        var reported = false;
        var quarantined = database.QuarantineIfCorrupt(_ => reported = true);

        Assert.False(quarantined);
        Assert.False(reported);
        Assert.True(File.Exists(database.DatabasePath));
    }

    [Fact]
    public void QuarantineIfCorrupt_NoFile_ReturnsFalse()
    {
        Assert.False(CreateDatabase().QuarantineIfCorrupt());
    }

    [Fact]
    public void StartupSequence_CorruptFile_IsQuarantinedThenDatabaseIsCreatedAndOpens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"startup.{Guid.NewGuid():N}.db");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });

        var quarantined = database.QuarantineIfCorrupt();
        database.EnsureCreated();
        database.Open();

        try
        {
            Assert.True(quarantined);
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
    public void Open_CalledTwice_StaysOpen()
    {
        var database = CreateDatabase();
        database.EnsureCreated();

        database.Open();
        database.Open();

        try
        {
            Assert.True(database.IsOpen);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void ConnectionsOpened_CountsEveryOpenedConnection()
    {
        var database = CreateDatabase();
        database.EnsureCreated();
        var before = database.ConnectionsOpened;

        using (database.OpenConnection())
        using (database.OpenConnection())
        {
        }

        Assert.Equal(2, database.ConnectionsOpened - before);
    }

    private static KillRightDatabase CreateDatabase(int? busyTimeoutSeconds = null, int? pageCacheMegabytes = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"instance.{Guid.NewGuid():N}.db");

        return new KillRightDatabase(new KillRightDatabaseOptions
        {
            DatabasePath = path,
            BusyTimeoutSeconds = busyTimeoutSeconds ?? KillRightDatabaseOptions.DefaultBusyTimeoutSeconds,
            PageCacheMegabytes = pageCacheMegabytes ?? KillRightDatabaseOptions.DefaultPageCacheMegabytes
        });
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;

        while ((current = Volatile.Read(ref location)) < value)
        {
            if (Interlocked.CompareExchange(ref location, value, current) == current)
                return;
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
