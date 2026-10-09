using System.Data;
using Killright.Shared.Data;
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
        var sdeMetadata = LoadSdeMetadata();

        return new DiagnosticsSummary
        {
            SdeBuildNumber = sdeMetadata.buildNumber,
            SdeLastCheckedUtc = sdeMetadata.lastCheckedUtc,
            SdeLastUpdatedUtc = sdeMetadata.lastUpdatedUtc,
            SdeLastCheckResult = sdeMetadata.lastCheckResult,
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
                WHERE is_qualifying = 0
                  AND kill_time_utc < {SqlValueFormatter.Date(ApplicationClock.UtcNow.AddDays(-14))};
                """),
            CurrentUtc = DateTimeOffset.UtcNow,
            EffectiveUtc = ApplicationClock.UtcNow,
            OffsetDays = ApplicationClock.OffsetDays,
            SchemaVersion = _database.GetSchemaVersion(),
            AlphaReleaseSchemaLocked = _database.GetAlphaLock(),
            QualificationFleetThresholdConfigured = App.Settings.QualificationFleetThreshold,
            QualificationFleetThresholdStored = _database.GetLastAppliedQualificationFleetThreshold()
        };
    }

    public DataTable LoadRows(string sql)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);

        return table;
    }

    public void ExecuteNonQuery(string sql)
    {
        using var scope = _database.BeginWrite();

        using var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();

        scope.Commit();
    }

    private (long? buildNumber, DateTimeOffset? lastCheckedUtc, DateTimeOffset? lastUpdatedUtc, string? lastCheckResult) LoadSdeMetadata()
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT build_number, last_checked_utc, last_updated_utc, last_check_result
            FROM main.sde_metadata
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return (null, null, null, null);

        return (
            reader.GetNullableInt64(0),
            reader.GetNullableDateTimeOffset(1),
            reader.GetNullableDateTimeOffset(2),
            reader.GetNullableString(3));
    }

    private int ExecuteScalarInt(string sql)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = command.ExecuteScalar();
        return Convert.ToInt32(value);
    }
}