using System.Data;
using DuckDB.NET.Data;
using PilotIntel.Shared.Time;
using PilotIntel.Storage.Database;

namespace PilotIntel.UI.Diagnostics;

public sealed class DiagnosticsDataService
{
    private readonly PilotIntelDatabase _database;

    public DiagnosticsDataService(PilotIntelDatabase database)
    {
        _database = database;
    }

    public DiagnosticsSummary LoadSummary()
    {
        return new DiagnosticsSummary
        {
            IdentityCacheRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.pilot_identity_cache;"),
            ActivityCacheRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.zkill_activity_cache;"),
            RecentKillmailRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.zkill_recent_killmail_cache;"),
            DuplicateKillmailRows = ExecuteScalarInt("""
                SELECT COUNT(*)
                FROM (
                    SELECT killmail_id
                    FROM main.zkill_recent_killmail_cache
                    GROUP BY killmail_id
                    HAVING COUNT(*) > 1
                );
                """),
            ExpiredKillmailRows = ExecuteScalarInt($"""
                SELECT COUNT(*)
                FROM main.zkill_recent_killmail_cache
                WHERE kill_time_utc < '{ApplicationClock.UtcNow.AddDays(-7).UtcDateTime:O}';
                """),
            CurrentUtc = DateTimeOffset.UtcNow,
            EffectiveUtc = ApplicationClock.UtcNow,
            OffsetDays = ApplicationClock.OffsetDays
        };
    }

    public DataTable LoadRows(string sql)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);

        return table;
    }

    public void ExecuteNonQuery(string sql)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private int ExecuteScalarInt(string sql)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = command.ExecuteScalar();
        return Convert.ToInt32(value);
    }
}