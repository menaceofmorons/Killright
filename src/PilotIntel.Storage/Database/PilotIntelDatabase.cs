using DuckDB.NET.Data;

namespace PilotIntel.Storage.Database;

public sealed class PilotIntelDatabase
{
    private readonly PilotIntelDatabaseOptions _options;

    public PilotIntelDatabase(PilotIntelDatabaseOptions options)
    {
        _options = options;
    }

    public string ConnectionString => $"Data Source={_options.DatabasePath}";

    public void EnsureCreated()
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = new DuckDBConnection(ConnectionString);
        connection.Open();

        CreatePilotIdentityCache(connection);
        CreatezKillActivityCache(connection);
        CreateRecentKillmailCache(connection);
        CreatezKillStatisticsCache(connection);
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
    }

    private static void CreateRecentKillmailCache(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS main.zkill_recent_killmail_cache (
                                  killmail_id BIGINT PRIMARY KEY,
                                  killmail_hash TEXT,
                                  character_id BIGINT NOT NULL,
                                  kill_time_utc TEXT NOT NULL,
                                  is_loss BOOLEAN NOT NULL,
                                  attacker_count INTEGER NOT NULL,
                                  is_solo BOOLEAN NOT NULL,
                                  ship_type_id BIGINT,
                                  system_id BIGINT,
                                  location_id BIGINT,
                                  is_npc BOOLEAN NOT NULL,
                                  cached_at_utc TEXT NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
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
    }
}