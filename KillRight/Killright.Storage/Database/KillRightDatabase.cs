using DuckDB.NET.Data;
using Killright.Shared.Data;

namespace Killright.Storage.Database;

public sealed class KillRightDatabase
{
    public const int CurrentSchemaVersion = 2;

    private readonly KillRightDatabaseOptions _options;

    public KillRightDatabase(KillRightDatabaseOptions options)
    {
        _options = options;
    }

    private int _connectionsOpened;

    public string ConnectionString => $"Data Source={_options.DatabasePath}";

    public int ConnectionsOpened => Volatile.Read(ref _connectionsOpened);

    public DuckDBConnection OpenConnection()
    {
        var connection = new DuckDBConnection(ConnectionString);

        try
        {
            connection.Open();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        Interlocked.Increment(ref _connectionsOpened);

        return connection;
    }

    public ScanDatabaseSession OpenScanSession()
    {
        return new ScanDatabaseSession(OpenConnection());
    }

    public void EnsureCreated()
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        CreatePilotIdentityCache(connection);
        CreateEsiEntityNameCache(connection);
        CreatezKillActivityCache(connection);
        CreatezKillStatisticsCache(connection);
        CreateZkillKillmailsTables(connection);
        CreateSdeTables(connection);
        CreateSdeMetadata(connection);
        CreateSchemaMetadata(connection);
    }

    public bool EnsureCreatedWithRecovery(Action<string>? onCorruptionDetected = null)
    {
        try
        {
            EnsureCreated();
            return false;
        }
        catch (Exception ex)
        {
            onCorruptionDetected?.Invoke(ex.Message);
            QuarantineExistingDatabaseFiles();
            EnsureCreated();
            return true;
        }
    }

    public int GetSchemaVersion()
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM main.schema_metadata LIMIT 1;";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int? GetLastAppliedQualificationFleetThreshold()
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_qualification_fleet_threshold FROM main.schema_metadata LIMIT 1;";

        using var reader = command.ExecuteReader();

        return reader.Read() ? reader.GetNullableInt32(0) : null;
    }

    public void SetLastAppliedQualificationFleetThreshold(int threshold)
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE main.schema_metadata SET last_qualification_fleet_threshold = {threshold};";
        command.ExecuteNonQuery();
    }

    public bool GetAlphaLock()
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT alpha_lock FROM main.schema_metadata LIMIT 1;";

        return Convert.ToBoolean(command.ExecuteScalar());
    }

    public void SetAlphaLock()
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE main.schema_metadata SET alpha_lock = TRUE;";
        command.ExecuteNonQuery();
    }

    public void SetSchemaVersion(int version)
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE main.schema_metadata SET schema_version = {version};";
        command.ExecuteNonQuery();
    }

    public void RebuildKillmailAndAttackerTables()
    {
        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        using (var dropAttackers = connection.CreateCommand())
        {
            dropAttackers.CommandText = "DROP TABLE IF EXISTS main.zkill_killmail_attackers;";
            dropAttackers.ExecuteNonQuery();
        }

        using (var dropKillmails = connection.CreateCommand())
        {
            dropKillmails.CommandText = "DROP TABLE IF EXISTS main.zkill_killmails;";
            dropKillmails.ExecuteNonQuery();
        }

        CreateZkillKillmailsTables(connection);
    }

    private void QuarantineExistingDatabaseFiles()
    {
        if (!File.Exists(_options.DatabasePath))
            return;

        var quarantineSuffix = $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        File.Move(_options.DatabasePath, _options.DatabasePath + quarantineSuffix, overwrite: true);

        var walPath = _options.DatabasePath + ".wal";

        if (File.Exists(walPath))
            File.Move(walPath, walPath + quarantineSuffix, overwrite: true);
    }

    private static void CreatePilotIdentityCache(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.pilot_identity_cache (
                                  input_name TEXT PRIMARY KEY,
                                  character_id BIGINT,
                                  character_name TEXT,
                                  verify_status TEXT NOT NULL,
                                  security_status DOUBLE,
                                  corporation_id BIGINT,
                                  corporation_name TEXT,
                                  corporation_ticker TEXT,
                                  alliance_id BIGINT,
                                  alliance_name TEXT,
                                  alliance_ticker TEXT,
                                  cached_at_utc TIMESTAMP NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();

        using var addBirthday = connection.CreateCommand();
        addBirthday.CommandText = "ALTER TABLE main.pilot_identity_cache ADD COLUMN IF NOT EXISTS birthday DATE;";
        addBirthday.ExecuteNonQuery();

        using var addSecurityStatusAtUtc = connection.CreateCommand();
        addSecurityStatusAtUtc.CommandText = "ALTER TABLE main.pilot_identity_cache ADD COLUMN IF NOT EXISTS security_status_at_utc TIMESTAMP;";
        addSecurityStatusAtUtc.ExecuteNonQuery();

        using var migrateSecurityStatusAtUtc = connection.CreateCommand();
        migrateSecurityStatusAtUtc.CommandText = """
                              UPDATE main.pilot_identity_cache
                              SET security_status_at_utc = cached_at_utc
                              WHERE security_status_at_utc IS NULL AND security_status IS NOT NULL;
                              """;
        migrateSecurityStatusAtUtc.ExecuteNonQuery();
    }

    private static void CreateEsiEntityNameCache(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.esi_entity_name_cache (
                                  entity_id BIGINT PRIMARY KEY,
                                  entity_type TEXT NOT NULL,
                                  name TEXT NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void CreatezKillActivityCache(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.zkill_activity_cache (
                                  character_id BIGINT PRIMARY KEY,
                                  has_public_activity_data BOOLEAN NOT NULL,
                                  kills_week INTEGER,
                                  solo_week INTEGER,
                                  last_active_utc TEXT,
                                  last_activity_type TEXT,
                                  checked_at_utc TEXT NOT NULL,
                                  error TEXT
                              );
                              """;
        command.ExecuteNonQuery();

        using var addLastRecentCallUtc = connection.CreateCommand();
        addLastRecentCallUtc.CommandText = "ALTER TABLE main.zkill_activity_cache ADD COLUMN IF NOT EXISTS last_recent_call_utc TEXT;";
        addLastRecentCallUtc.ExecuteNonQuery();

        using var addRecentCoverageStartUtc = connection.CreateCommand();
        addRecentCoverageStartUtc.CommandText = "ALTER TABLE main.zkill_activity_cache ADD COLUMN IF NOT EXISTS recent_coverage_start_utc TEXT;";
        addRecentCoverageStartUtc.ExecuteNonQuery();
    }

    private static void CreatezKillStatisticsCache(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.zkill_statistics_cache (
                                  character_id BIGINT PRIMARY KEY,
                                  ships_destroyed INTEGER NOT NULL,
                                  solo_kills INTEGER NOT NULL,
                                  solo_ratio DOUBLE NOT NULL,
                                  avg_gang_size DOUBLE NOT NULL,
                                  ships_lost INTEGER NOT NULL,
                                  solo_losses INTEGER NOT NULL,
                                  general_style TEXT NOT NULL,
                                  checked_at_utc TEXT NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();

        using var addMonthsProcessed = connection.CreateCommand();
        addMonthsProcessed.CommandText = "ALTER TABLE main.zkill_statistics_cache ADD COLUMN IF NOT EXISTS months_processed BOOLEAN;";
        addMonthsProcessed.ExecuteNonQuery();

        using var addNoHistoryMarker = connection.CreateCommand();
        addNoHistoryMarker.CommandText = "ALTER TABLE main.zkill_statistics_cache ADD COLUMN IF NOT EXISTS no_history_marker BOOLEAN;";
        addNoHistoryMarker.ExecuteNonQuery();

        using var addPodKills = connection.CreateCommand();
        addPodKills.CommandText = "ALTER TABLE main.zkill_statistics_cache ADD COLUMN IF NOT EXISTS pod_kills INTEGER;";
        addPodKills.ExecuteNonQuery();
    }

    private static void CreateZkillKillmailsTables(DuckDBConnection connection)
    {
        using var killmailsCommand = connection.CreateCommand();
        killmailsCommand.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.zkill_killmails (
                                  killmail_id BIGINT PRIMARY KEY,
                                  killmail_hash TEXT,
                                  kill_time_utc TEXT NOT NULL,
                                  system_id BIGINT NOT NULL,
                                  location_id BIGINT,
                                  victim_character_id BIGINT,
                                  victim_ship_type_id BIGINT,
                                  unique_attacker_count INTEGER NOT NULL,
                                  is_solo BOOLEAN NOT NULL,
                                  is_npc BOOLEAN NOT NULL,
                                  is_qualifying BOOLEAN NOT NULL,
                                  cached_at_utc TEXT NOT NULL
                              );
                              """;
        killmailsCommand.ExecuteNonQuery();

        using var attackersCommand = connection.CreateCommand();
        attackersCommand.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.zkill_killmail_attackers (
                                  killmail_id BIGINT NOT NULL,
                                  character_id BIGINT NOT NULL,
                                  corporation_id BIGINT,
                                  alliance_id BIGINT,
                                  ship_type_id BIGINT,
                                  PRIMARY KEY (killmail_id, character_id)
                              );
                              """;
        attackersCommand.ExecuteNonQuery();

        using var addWeaponTypeId = connection.CreateCommand();
        addWeaponTypeId.CommandText = "ALTER TABLE main.zkill_killmail_attackers ADD COLUMN IF NOT EXISTS weapon_type_id BIGINT;";
        addWeaponTypeId.ExecuteNonQuery();
    }

    private static void CreateSdeTables(DuckDBConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS main.sde_types (
                                      type_id BIGINT PRIMARY KEY,
                                      name TEXT NOT NULL
                                  );
                                  """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS main.sde_solar_systems (
                                      system_id BIGINT PRIMARY KEY,
                                      name TEXT NOT NULL
                                  );
                                  """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS main.sde_npc_corporations (
                                      corporation_id BIGINT PRIMARY KEY
                                  );
                                  """;
            command.ExecuteNonQuery();
        }
    }

    private static void CreateSdeMetadata(DuckDBConnection connection)
    {
        using (var createTable = connection.CreateCommand())
        {
            createTable.CommandText = """
                                  CREATE TABLE IF NOT EXISTS main.sde_metadata (
                                      build_number BIGINT,
                                      last_checked_utc TEXT,
                                      last_updated_utc TEXT,
                                      last_check_result TEXT
                                  );
                                  """;
            createTable.ExecuteNonQuery();
        }

        using (var addLastAttemptUtc = connection.CreateCommand())
        {
            addLastAttemptUtc.CommandText = "ALTER TABLE main.sde_metadata ADD COLUMN IF NOT EXISTS last_attempt_utc TEXT;";
            addLastAttemptUtc.ExecuteNonQuery();
        }

        using (var insertIfAbsent = connection.CreateCommand())
        {
            insertIfAbsent.CommandText = """
                                  INSERT INTO main.sde_metadata (build_number, last_checked_utc, last_updated_utc, last_check_result)
                                  SELECT NULL, NULL, NULL, NULL
                                  WHERE NOT EXISTS (SELECT 1 FROM main.sde_metadata);
                                  """;
            insertIfAbsent.ExecuteNonQuery();
        }
    }

    private static void CreateSchemaMetadata(DuckDBConnection connection)
    {
        using var createTable = connection.CreateCommand();
        createTable.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.schema_metadata (
                                  schema_version INTEGER NOT NULL
                              );
                              """;
        createTable.ExecuteNonQuery();

        using var addLastQualificationFleetThreshold = connection.CreateCommand();
        addLastQualificationFleetThreshold.CommandText = "ALTER TABLE main.schema_metadata ADD COLUMN IF NOT EXISTS last_qualification_fleet_threshold INTEGER;";
        addLastQualificationFleetThreshold.ExecuteNonQuery();

        using var addAlphaLock = connection.CreateCommand();
        addAlphaLock.CommandText = "ALTER TABLE main.schema_metadata ADD COLUMN IF NOT EXISTS alpha_lock BOOLEAN DEFAULT FALSE;";
        addAlphaLock.ExecuteNonQuery();

        using var insertIfAbsent = connection.CreateCommand();
        insertIfAbsent.CommandText = $"""
                              INSERT INTO main.schema_metadata (schema_version)
                              SELECT {CurrentSchemaVersion}
                              WHERE NOT EXISTS (SELECT 1 FROM main.schema_metadata);
                              """;
        insertIfAbsent.ExecuteNonQuery();
    }
}