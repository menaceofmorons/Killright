using System.Data;
using DuckDB.NET.Data;
using Killright.Shared.Time;
using Killright.Storage.Database;

namespace Killright.UI.Diagnostics;

public sealed class DiagnosticsDataService
{
    private readonly KillRightDatabase _database;

    public DiagnosticsDataService(KillRightDatabase database)
    {
        _database = database;
    }

    public DiagnosticsSummary LoadSummary()
    {
        return new DiagnosticsSummary
        {
            IdentityCacheRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.pilot_identity_cache;"),
            ActivityCacheRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.zkill_activity_cache;"),
            RecentKillmailRows = ExecuteScalarInt("SELECT COUNT(*) FROM main.zkill_killmails;"),
            DuplicateKillmailRows = ExecuteScalarInt("""
                SELECT COUNT(*)
                FROM (
                    SELECT killmail_id
                    FROM main.zkill_killmails
                    GROUP BY killmail_id
                    HAVING COUNT(*) > 1
                );
                """),
            ExpiredKillmailRows = ExecuteScalarInt($"""
                SELECT COUNT(*)
                FROM main.zkill_killmails
                WHERE is_qualifying = FALSE
                  AND kill_time_utc < '{ApplicationClock.UtcNow.AddDays(-14).UtcDateTime:O}';
                """),
            CurrentUtc = DateTimeOffset.UtcNow,
            EffectiveUtc = ApplicationClock.UtcNow,
            OffsetDays = ApplicationClock.OffsetDays,
            SchemaVersion = _database.GetSchemaVersion(),
            AlphaReleaseSchemaLocked = App.Settings.AlphaReleaseSchemaLocked
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