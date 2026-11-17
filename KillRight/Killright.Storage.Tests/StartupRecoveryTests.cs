using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class StartupRecoveryTests
{
    public static TheoryData<string> GuardedSteps =>
    [
        "quarantine",
        "exists",
        "restore",
        "open",
        "schema",
        "requalify"
    ];

    [Theory]
    [MemberData(nameof(GuardedSteps))]
    public void Run_StepThrows_YieldsRefusalAndRecordsIt(string failingStep)
    {
        var records = new List<string>();
        var steps = HealthySteps(failingStep);

        var result = GuardedStartup.Run(steps, records.Add);

        Assert.Equal(StartupOutcome.DatabaseRefused, result.Outcome);
        Assert.Contains(records, record => record.StartsWith("Startup refused", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_HealthySteps_IsReady()
    {
        var result = GuardedStartup.Run(HealthySteps(null), _ => { });

        Assert.Equal(StartupOutcome.Ready, result.Outcome);
    }

    [Fact]
    public void Run_SchemaCheckRefuses_ReturnsSchemaRefusedWithoutRequalifying()
    {
        var requalified = false;
        var steps = HealthySteps(null) with
        {
            CheckSchema = () => new SchemaVersionCheckResult(SchemaVersionCheckOutcome.RefusedDowngrade, 9, 4),
            Requalify = () => requalified = true
        };

        var result = GuardedStartup.Run(steps, _ => { });

        Assert.Equal(StartupOutcome.SchemaRefused, result.Outcome);
        Assert.Equal(9, result.Schema?.StoredVersion);
        Assert.False(requalified);
    }

    [Fact]
    public void Run_OpenFailsOnce_SetsFileAsideRestoresAndReopens()
    {
        var calls = new List<string>();
        var opens = 0;
        var steps = HealthySteps(null) with
        {
            EnsureCreatedAndOpen = () =>
            {
                calls.Add("open");

                if (opens++ == 0)
                    throw new InvalidOperationException("The database could not enter WAL journal mode.");
            },
            SetAside = () => calls.Add("setAside"),
            DatabaseExists = () => !calls.Contains("setAside"),
            TryRestore = () =>
            {
                calls.Add("restore");
                return true;
            }
        };

        var result = GuardedStartup.Run(steps, _ => { });

        Assert.Equal(StartupOutcome.Ready, result.Outcome);
        Assert.Equal(["open", "setAside", "restore", "open"], calls);
    }

    [Fact]
    public void Run_DatabaseBusy_RefusesWithoutSettingTheFileAside()
    {
        var setAside = false;
        var steps = HealthySteps(null) with
        {
            EnsureCreatedAndOpen = () => throw new SqliteException("database is locked", 5),
            SetAside = () => setAside = true
        };

        var result = GuardedStartup.Run(steps, _ => { });

        Assert.Equal(StartupOutcome.DatabaseRefused, result.Outcome);
        Assert.False(setAside);
    }

    [Fact]
    public async Task Run_LiveDatabaseFailsQuickCheck_RestoresTheLatestReadableBackup()
    {
        var fixture = CreateFixture();
        InsertKillmail(fixture.Database, 810001);
        await fixture.Service.BackupAsync();
        AddBulkRows(fixture.Database);
        fixture.Database.Close();
        DamageLastPage(fixture.Database.DatabasePath);

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = fixture.Database.DatabasePath });
        var service = new DatabaseBackupService(database, fixture.Folder, 5);
        var records = new List<string>();

        var result = GuardedStartup.Run(database, service, isAlphaRelease: false, qualificationFleetThreshold: 11, records.Add);

        try
        {
            Assert.Equal(StartupOutcome.Ready, result.Outcome);
            Assert.Contains(records, record => record.Contains("latest backup", StringComparison.Ordinal));
            Assert.Equal(1L, CountRows(database.DatabasePath, "zkill_killmails"));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(database.DatabasePath)!, $"{Path.GetFileName(database.DatabasePath)}.corrupt-*"));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void Run_LiveDatabaseFailsQuickCheckAndNoReadableBackup_CreatesNewDatabase()
    {
        var fixture = CreateFixture();
        AddBulkRows(fixture.Database);
        fixture.Database.Close();
        DamageLastPage(fixture.Database.DatabasePath);

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = fixture.Database.DatabasePath });
        var service = new DatabaseBackupService(database, fixture.Folder, 5);

        var result = GuardedStartup.Run(database, service, isAlphaRelease: false, qualificationFleetThreshold: 11, _ => { });

        try
        {
            Assert.Equal(StartupOutcome.Ready, result.Outcome);
            Assert.True(database.IsOpen);
            Assert.Equal(0L, CountRows(database.DatabasePath, "zkill_killmails"));
            Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        }
        finally
        {
            database.Close();
        }
    }

    private static StartupSteps HealthySteps(string? failingStep)
    {
        T Guard<T>(string name, T value)
        {
            if (name == failingStep)
                throw new InvalidOperationException($"{name} failed");

            return value;
        }

        return new StartupSteps(
            _ => Guard("quarantine", false),
            () => Guard("exists", failingStep != "restore"),
            () => Guard("restore", false),
            () => { },
            () => Guard("open", 0),
            () => Guard("schema", new SchemaVersionCheckResult(SchemaVersionCheckOutcome.Ok, 4, 4)),
            () => Guard("requalify", 0));
    }

    private static (KillRightDatabase Database, string Folder, DatabaseBackupService Service) CreateFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"startupRecovery.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        database.Open();

        var folder = Path.Combine(Path.GetTempPath(), $"startupRecoveryBackups.{Guid.NewGuid():N}");

        return (database, folder, new DatabaseBackupService(database, folder, 5));
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

    private static void AddBulkRows(KillRightDatabase database)
    {
        Execute(
            database,
            "CREATE TABLE main.bulk (id INTEGER PRIMARY KEY, payload TEXT NOT NULL); "
            + "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2000) "
            + "INSERT INTO main.bulk SELECT i, hex(randomblob(64)) FROM n;");
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

    private static long CountRows(string databasePath, string table)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{table};";

        return Convert.ToInt64(command.ExecuteScalar());
    }
}
