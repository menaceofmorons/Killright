using Killright.Shared.Data;
using Killright.Storage.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

public sealed class KillRightDatabase
{
    public const int CurrentSchemaVersion = 4;

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    private readonly KillRightDatabaseOptions _options;
    private readonly string _connectionString;
    private readonly object _stateGate = new();
    private InstanceState _state = InstanceState.NeverOpened;
    private int _connectionsOpened;

    public KillRightDatabase(KillRightDatabaseOptions options)
    {
        _options = options;
        _connectionString = BuildConnectionString(options, SqliteOpenMode.ReadWriteCreate, pooling: true);
    }

    private enum InstanceState
    {
        NeverOpened,
        Open,
        Closed
    }

    public string DatabasePath => _options.DatabasePath;

    public string ConnectionString => _connectionString;

    public int ConnectionsOpened => Volatile.Read(ref _connectionsOpened);

    public bool IsOpen
    {
        get
        {
            lock (_stateGate)
                return _state == InstanceState.Open;
        }
    }

    public void Open()
    {
        lock (_stateGate)
        {
            if (_state == InstanceState.Open)
                return;
        }

        using (var connection = CreateConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL;";

            var mode = Convert.ToString(command.ExecuteScalar());

            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The database could not enter WAL journal mode (reported '{mode}').");
        }

        lock (_stateGate)
            _state = InstanceState.Open;
    }

    public void Close()
    {
        lock (_stateGate)
            _state = InstanceState.Closed;

        SqliteConnection.ClearAllPools();
    }

    public void Checkpoint()
    {
        WriteGate.Wait();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public bool HasPendingWal()
    {
        var wal = new FileInfo(_options.DatabasePath + "-wal");

        return wal.Exists && wal.Length > 0;
    }

    public SqliteConnection OpenConnection()
    {
        lock (_stateGate)
        {
            if (_state == InstanceState.Closed)
                throw new InvalidOperationException("The database instance is closed.");
        }

        return CreateConnection();
    }

    public ScanDatabaseSession OpenScanSession()
    {
        return new ScanDatabaseSession(this, OpenConnection());
    }

    public WriteScope BeginWrite(SqliteConnection? connection = null, ScanTimings? timings = null, string? tag = null)
    {
        using (timings.Measure(ScanTimings.ScanLevel, "write_gate_wait", null, tag))
            WriteGate.Wait();

        return StartWriteScope(connection);
    }

    public async Task<WriteScope> BeginWriteAsync(SqliteConnection? connection = null, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken);

        return StartWriteScope(connection);
    }

    public bool QuarantineIfCorrupt(Action<string>? onCorruptionDetected = null)
    {
        if (!File.Exists(_options.DatabasePath))
            return false;

        try
        {
            using var connection = new SqliteConnection(BuildConnectionString(_options, SqliteOpenMode.ReadOnly, pooling: false));
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema;";
            command.ExecuteScalar();

            return false;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            onCorruptionDetected?.Invoke(exception.Message);

            var quarantineSuffix = $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

            foreach (var path in new[] { _options.DatabasePath, _options.DatabasePath + "-wal", _options.DatabasePath + "-shm" })
            {
                if (File.Exists(path))
                    File.Move(path, path + quarantineSuffix, overwrite: true);
            }

            return true;
        }
    }

    public void EnsureCreated()
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = CreateConnection();

        if (ReadScalarLong(connection, "SELECT COUNT(*) FROM sqlite_schema;") == 0)
        {
            ExecuteNonQuery(connection, "PRAGMA auto_vacuum = INCREMENTAL;");
            ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
        }

        CreatePilotIdentityCache(connection);
        CreateEsiEntityNameCache(connection);
        CreatezKillActivityCache(connection);
        CreatezKillStatisticsCache(connection);
        CreatePilotLastKillmailCache(connection);
        CreateZkillKillmailsTables(connection);
        CreateSdeTables(connection);
        CreateSdeMetadata(connection);
        CreateSchemaMetadata(connection);
    }

    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM main.schema_metadata LIMIT 1;";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int? GetLastAppliedQualificationFleetThreshold()
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_qualification_fleet_threshold FROM main.schema_metadata LIMIT 1;";

        using var reader = command.ExecuteReader();

        return reader.Read() ? reader.GetNullableInt32(0) : null;
    }

    public void SetLastAppliedQualificationFleetThreshold(int threshold)
    {
        ExecuteWrite($"UPDATE main.schema_metadata SET last_qualification_fleet_threshold = {threshold};");
    }

    public bool GetAlphaLock()
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT alpha_lock FROM main.schema_metadata LIMIT 1;";

        return Convert.ToBoolean(command.ExecuteScalar());
    }

    public void SetAlphaLock()
    {
        ExecuteWrite("UPDATE main.schema_metadata SET alpha_lock = 1;");
    }

    public void SetSchemaVersion(int version)
    {
        ExecuteWrite($"UPDATE main.schema_metadata SET schema_version = {version};");
    }

    public void RebuildKillmailAndAttackerTables()
    {
        using var scope = BeginWrite();

        ExecuteNonQuery(scope.Connection, scope.Transaction, "DROP TABLE IF EXISTS main.zkill_killmail_attackers;");
        ExecuteNonQuery(scope.Connection, scope.Transaction, "DROP TABLE IF EXISTS main.zkill_killmails;");

        CreateZkillKillmailsTables(scope.Connection, scope.Transaction);

        scope.Commit();
    }

    private static string BuildConnectionString(KillRightDatabaseOptions options, SqliteOpenMode mode, bool pooling)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = pooling,
            DefaultTimeout = KillRightDatabaseOptions.NormalizeBusyTimeoutSeconds(options.BusyTimeoutSeconds)
        }.ToString();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);

        try
        {
            connection.Open();

            var busyTimeoutMilliseconds = KillRightDatabaseOptions.NormalizeBusyTimeoutSeconds(_options.BusyTimeoutSeconds) * 1000;
            var cacheKibibytes = KillRightDatabaseOptions.NormalizePageCacheMegabytes(_options.PageCacheMegabytes) * 1024;

            ExecuteNonQuery(
                connection,
                $"""
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = {busyTimeoutMilliseconds};
                PRAGMA cache_size = -{cacheKibibytes};
                PRAGMA temp_store = MEMORY;
                """);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        Interlocked.Increment(ref _connectionsOpened);

        return connection;
    }

    private WriteScope StartWriteScope(SqliteConnection? connection)
    {
        SqliteConnection? ownedConnection = null;

        try
        {
            ownedConnection = connection is null ? OpenConnection() : null;
            var target = connection ?? ownedConnection!;
            var transaction = target.BeginTransaction(deferred: false);

            return new WriteScope(target, transaction, ownedConnection, WriteGate);
        }
        catch
        {
            ownedConnection?.Dispose();
            WriteGate.Release();
            throw;
        }
    }

    private void ExecuteWrite(string sql)
    {
        using var scope = BeginWrite();

        ExecuteNonQuery(scope.Connection, scope.Transaction, sql);

        scope.Commit();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ReadScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void CreatePilotIdentityCache(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.pilot_identity_cache (
                input_name TEXT NOT NULL PRIMARY KEY,
                character_id INTEGER,
                character_name TEXT,
                verify_status TEXT NOT NULL,
                security_status REAL,
                corporation_id INTEGER,
                corporation_name TEXT,
                corporation_ticker TEXT,
                alliance_id INTEGER,
                alliance_name TEXT,
                alliance_ticker TEXT,
                cached_at_utc INTEGER NOT NULL,
                birthday INTEGER,
                security_status_at_utc INTEGER,
                faction_id INTEGER
            ) STRICT;
            """);
    }

    private static void CreateEsiEntityNameCache(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.esi_entity_name_cache (
                entity_id INTEGER NOT NULL PRIMARY KEY,
                entity_type TEXT NOT NULL,
                name TEXT NOT NULL
            ) STRICT;
            """);
    }

    private static void CreatezKillActivityCache(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.zkill_activity_cache (
                character_id INTEGER NOT NULL PRIMARY KEY,
                has_public_activity_data INTEGER NOT NULL CHECK (has_public_activity_data IN (0, 1)),
                kills_week INTEGER,
                solo_week INTEGER,
                last_active_utc INTEGER,
                last_activity_type TEXT,
                checked_at_utc INTEGER NOT NULL,
                error TEXT,
                last_recent_call_utc INTEGER,
                recent_coverage_start_utc INTEGER,
                last_kill_utc INTEGER
            ) STRICT;
            """);
    }

    private static void CreatezKillStatisticsCache(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.zkill_statistics_cache (
                character_id INTEGER NOT NULL PRIMARY KEY,
                ships_destroyed INTEGER NOT NULL,
                solo_kills INTEGER NOT NULL,
                solo_ratio REAL NOT NULL,
                avg_gang_size REAL NOT NULL,
                ships_lost INTEGER NOT NULL,
                solo_losses INTEGER NOT NULL,
                general_style TEXT NOT NULL,
                checked_at_utc INTEGER NOT NULL,
                months_processed INTEGER CHECK (months_processed IN (0, 1)),
                no_history_marker INTEGER CHECK (no_history_marker IN (0, 1)),
                pod_kills INTEGER,
                pod_losses INTEGER
            ) STRICT;
            """);
    }

    private static void CreatePilotLastKillmailCache(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.pilot_last_killmail_cache (
                character_id INTEGER NOT NULL PRIMARY KEY,
                has_killmail INTEGER NOT NULL CHECK (has_killmail IN (0, 1)),
                killmail_id INTEGER,
                kill_time_utc INTEGER,
                activity_type TEXT,
                system_id INTEGER,
                ship_type_id INTEGER,
                victim_ship_type_id INTEGER,
                attacker_count INTEGER,
                weapon_type_id INTEGER,
                checked_at_utc INTEGER NOT NULL
            ) STRICT;
            """);
    }

    private static void CreateZkillKillmailsTables(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        const string killmails = """
            CREATE TABLE IF NOT EXISTS main.zkill_killmails (
                killmail_id INTEGER NOT NULL PRIMARY KEY,
                killmail_hash TEXT,
                kill_time_utc INTEGER NOT NULL,
                system_id INTEGER NOT NULL,
                location_id INTEGER,
                victim_character_id INTEGER,
                victim_ship_type_id INTEGER,
                unique_attacker_count INTEGER NOT NULL,
                is_solo INTEGER NOT NULL CHECK (is_solo IN (0, 1)),
                is_npc INTEGER NOT NULL CHECK (is_npc IN (0, 1)),
                is_qualifying INTEGER NOT NULL CHECK (is_qualifying IN (0, 1)),
                cached_at_utc INTEGER NOT NULL
            ) STRICT;
            """;

        const string attackers = """
            CREATE TABLE IF NOT EXISTS main.zkill_killmail_attackers (
                killmail_id INTEGER NOT NULL,
                character_id INTEGER NOT NULL,
                corporation_id INTEGER,
                alliance_id INTEGER,
                ship_type_id INTEGER,
                weapon_type_id INTEGER,
                PRIMARY KEY (killmail_id, character_id),
                FOREIGN KEY (killmail_id) REFERENCES zkill_killmails (killmail_id) ON DELETE CASCADE
            ) STRICT;
            """;

        string[] statements =
        [
            killmails,
            attackers,
            "CREATE INDEX IF NOT EXISTS main.ix_zkill_killmail_attackers_character_id ON zkill_killmail_attackers (character_id);",
            "CREATE INDEX IF NOT EXISTS main.ix_zkill_killmails_victim_character_id ON zkill_killmails (victim_character_id);",
            "CREATE INDEX IF NOT EXISTS main.ix_zkill_killmails_kill_time_utc ON zkill_killmails (kill_time_utc);"
        ];

        foreach (var statement in statements)
        {
            if (transaction is null)
                ExecuteNonQuery(connection, statement);
            else
                ExecuteNonQuery(connection, transaction, statement);
        }
    }

    private static void CreateSdeTables(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.sde_types (
                type_id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL
            ) STRICT;
            """);

        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.sde_solar_systems (
                system_id INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL
            ) STRICT;
            """);

        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.sde_npc_corporations (
                corporation_id INTEGER NOT NULL PRIMARY KEY
            ) STRICT;
            """);

        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.sde_factions (
                faction_id INTEGER NOT NULL PRIMARY KEY,
                name TEXT
            ) STRICT;
            """);
    }

    private static void CreateSdeMetadata(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.sde_metadata (
                build_number INTEGER,
                last_checked_utc INTEGER,
                last_updated_utc INTEGER,
                last_check_result TEXT,
                last_attempt_utc INTEGER
            ) STRICT;
            """);

        ExecuteNonQuery(
            connection,
            """
            INSERT INTO main.sde_metadata (build_number, last_checked_utc, last_updated_utc, last_check_result, last_attempt_utc)
            SELECT NULL, NULL, NULL, NULL, NULL
            WHERE NOT EXISTS (SELECT 1 FROM main.sde_metadata);
            """);
    }

    private static void CreateSchemaMetadata(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS main.schema_metadata (
                schema_version INTEGER NOT NULL,
                last_qualification_fleet_threshold INTEGER,
                alpha_lock INTEGER NOT NULL DEFAULT 0 CHECK (alpha_lock IN (0, 1))
            ) STRICT;
            """);

        ExecuteNonQuery(
            connection,
            $"""
            INSERT INTO main.schema_metadata (schema_version)
            SELECT {CurrentSchemaVersion}
            WHERE NOT EXISTS (SELECT 1 FROM main.schema_metadata);
            """);
    }
}
