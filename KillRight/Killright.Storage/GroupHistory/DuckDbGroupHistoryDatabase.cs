using DuckDB.NET.Data;
using Killright.Storage.GroupHistory.Models;

namespace Killright.Storage.GroupHistory;

public sealed class DuckDbGroupHistoryDatabase : IGroupHistoryDatabase
{
    public DuckDbGroupHistoryDatabase(string? databasePath = null)
    {
        DatabasePath = databasePath ?? GroupHistoryDatabasePaths.GetDefaultDatabasePath();
    }

    public string DatabasePath { get; }

    public async Task<GroupHistoryDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DatabasePath))
            return new GroupHistoryDatabaseStatus(false, false, null, null, null, null, null, null, null);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(connection, "history_metadata", cancellationToken))
            return new GroupHistoryDatabaseStatus(true, false, null, null, null, null, null, null, null);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_version,
                   history_start_day_utc,
                   last_completed_day_utc,
                   created_utc,
                   last_update_utc,
                   average_import_milliseconds_per_day,
                   last_import_result
            FROM history_metadata
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return new GroupHistoryDatabaseStatus(true, true, null, null, null, null, null, null, null);

        return new GroupHistoryDatabaseStatus(
            true,
            true,
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : DateOnly.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : DateOnly.Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, GetSchemaSql(), cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_metadata
            (schema_version, history_start_day_utc, last_completed_day_utc, created_utc, last_update_utc,
             average_import_milliseconds_per_day, last_import_result)
            SELECT $schema_version, NULL, NULL, $created_utc, NULL, NULL, 'Created'
            WHERE NOT EXISTS (SELECT 1 FROM history_metadata);
            """;
        command.Parameters.Add(new DuckDBParameter("schema_version", GroupHistoryConstants.SchemaVersion));
        command.Parameters.Add(new DuckDBParameter("created_utc", DateTime.UtcNow.ToString("O")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<GroupHistoryUpdateRequirement> GetUpdateRequirementAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        var latestImportableDay = DateOnly.FromDateTime(utcNow.Date.AddDays(-1));

        if (!status.DatabaseExists || !status.SchemaExists)
        {
            var firstImportDay = latestImportableDay.AddYears(-GroupHistoryConstants.InitialHistoricImportHorizonYears);
            return new GroupHistoryUpdateRequirement(status.DatabaseExists, status.SchemaExists, true, firstImportDay,
                latestImportableDay, CountDaysInclusive(firstImportDay, latestImportableDay), TimeSpan.FromMinutes(40), true,
                "Historic Group Detection database has not been created.");
        }

        if (status.LastCompletedDayUtc is null)
        {
            return new GroupHistoryUpdateRequirement(true, true, false, null, null, 0, TimeSpan.Zero, false,
                "Historic Group Detection database schema exists, but no history has been imported yet.");
        }

        if (status.LastCompletedDayUtc.Value >= latestImportableDay)
        {
            return new GroupHistoryUpdateRequirement(true, true, false, null, null, 0, TimeSpan.Zero, false,
                "Historic Group Detection database is current.");
        }

        var firstMissingDay = status.LastCompletedDayUtc.Value.AddDays(1);
        var missingDayCount = CountDaysInclusive(firstMissingDay, latestImportableDay);
        var millisecondsPerDay = status.AverageImportMillisecondsPerDay ?? 500;
        var estimatedDuration = TimeSpan.FromMilliseconds(millisecondsPerDay * missingDayCount);
        var promptRequired = estimatedDuration > TimeSpan.FromMinutes(GroupHistoryConstants.PromptThresholdMinutes);

        return new GroupHistoryUpdateRequirement(true, true, false, firstMissingDay, latestImportableDay, missingDayCount,
            estimatedDuration, promptRequired,
            $"Historic Group Detection database is missing {missingDayCount} completed UTC day(s).");
    }

    public async Task<bool> IsImportDayCompletedAsync(DateOnly importDateUtc, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DatabasePath))
            return false;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(connection, "history_import_day_status", cancellationToken))
            return false;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM history_import_day_status
            WHERE import_date_utc = $import_date_utc
              AND status = 'Completed';
            """;
        command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return ConvertScalarToLong(result) > 0;
    }

    public async Task MarkImportDayStartedAsync(DateOnly importDateUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO history_import_day_status
            (import_date_utc, status, started_utc, completed_utc, raw_killmail_count,
             qualifying_killmail_count, participant_index_row_count, pair_occurrence_count, error_message)
            VALUES ($import_date_utc, 'InProgress', $started_utc, NULL, 0, 0, 0, 0, NULL);
            """;
        command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
        command.Parameters.Add(new DuckDBParameter("started_utc", DateTime.UtcNow.ToString("O")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkImportDayCompletedAsync(DateOnly importDateUtc, int rawKillmailCount, int qualifyingKillmailCount,
        int participantIndexRowCount, long pairOccurrenceCount, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await MarkImportDayCompletedCoreAsync(connection, transaction, importDateUtc, rawKillmailCount, qualifyingKillmailCount,
            participantIndexRowCount, pairOccurrenceCount, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkImportDayFailedAsync(DateOnly importDateUtc, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO history_import_day_status
            (import_date_utc, status, started_utc, completed_utc, raw_killmail_count,
             qualifying_killmail_count, participant_index_row_count, pair_occurrence_count, error_message)
            VALUES ($import_date_utc, 'Failed', NULL, $completed_utc, 0, 0, 0, 0, $error_message);
            """;
        command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
        command.Parameters.Add(new DuckDBParameter("completed_utc", DateTime.UtcNow.ToString("O")));
        command.Parameters.Add(new DuckDBParameter("error_message", errorMessage));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<GroupHistorySummaryBuildResult> ImportEvidenceAndParticipantRowsAndUpdateSummaryForDayAsync(
        DateOnly importDateUtc,
        IReadOnlyList<GroupHistoryEvidenceImportRow> evidenceRows,
        IReadOnlyList<GroupHistoryParticipantImportRow> participantRows,
        int rawKillmailCount,
        int qualifyingKillmailCount,
        int qualifyingAttackerCount,
        long candidatePairOccurrenceRows,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in evidenceRows)
            await InsertEvidenceRowAsync(connection, transaction, row, cancellationToken);

        foreach (var row in participantRows)
            await InsertParticipantRowAsync(connection, transaction, row, cancellationToken);

        var summaryResult = await UpsertRelationshipSummaryForDayAsync(connection, transaction, importDateUtc, cancellationToken);

        await MarkImportDayCompletedCoreAsync(connection, transaction, importDateUtc, rawKillmailCount, qualifyingKillmailCount,
            qualifyingAttackerCount, candidatePairOccurrenceRows, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return summaryResult;
    }

    private static async Task<GroupHistorySummaryBuildResult> UpsertRelationshipSummaryForDayAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        DateOnly importDateUtc,
        CancellationToken cancellationToken)
    {
        var importDateText = importDateUtc.ToString("yyyy-MM-dd");
        var rebuiltUtc = DateTime.UtcNow.ToString("O");

        var evidenceRows = await ExecuteScalarLongAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM historic_relationship_evidence
            WHERE evidence_date_utc = $import_date_utc;
            """,
            importDateText,
            cancellationToken);

        var participantRows = await ExecuteScalarLongAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM historic_relationship_evidence_participants p
            JOIN historic_relationship_evidence e
              ON e.evidence_id = p.evidence_id
            WHERE e.evidence_date_utc = $import_date_utc;
            """,
            importDateText,
            cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO historic_relationship_summary
                (
                    pilot_a_id,
                    pilot_b_id,
                    shared_event_count,
                    first_seen_utc,
                    last_seen_utc,
                    last_rebuilt_utc
                )
                SELECT
                    p1.character_id AS pilot_a_id,
                    p2.character_id AS pilot_b_id,
                    COUNT(*) AS shared_event_count,
                    MIN(e.killmail_time_utc) AS first_seen_utc,
                    MAX(e.killmail_time_utc) AS last_seen_utc,
                    $last_rebuilt_utc AS last_rebuilt_utc
                FROM historic_relationship_evidence e
                JOIN historic_relationship_evidence_participants p1
                  ON p1.evidence_id = e.evidence_id
                JOIN historic_relationship_evidence_participants p2
                  ON p2.evidence_id = e.evidence_id
                 AND p1.character_id < p2.character_id
                WHERE e.evidence_date_utc = $import_date_utc
                GROUP BY p1.character_id, p2.character_id
                ON CONFLICT (pilot_a_id, pilot_b_id)
                DO UPDATE SET
                    shared_event_count = historic_relationship_summary.shared_event_count + excluded.shared_event_count,
                    first_seen_utc = CASE
                        WHEN excluded.first_seen_utc < historic_relationship_summary.first_seen_utc THEN excluded.first_seen_utc
                        ELSE historic_relationship_summary.first_seen_utc
                    END,
                    last_seen_utc = CASE
                        WHEN excluded.last_seen_utc > historic_relationship_summary.last_seen_utc THEN excluded.last_seen_utc
                        ELSE historic_relationship_summary.last_seen_utc
                    END,
                    last_rebuilt_utc = excluded.last_rebuilt_utc;
                """;
            command.Parameters.Add(new DuckDBParameter("last_rebuilt_utc", rebuiltUtc));
            command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateText));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var pairOccurrenceRows = await ExecuteScalarLongAsync(
            connection,
            transaction,
            """
            SELECT COALESCE(SUM(shared_event_count), 0)
            FROM (
                SELECT COUNT(*) AS shared_event_count
                FROM historic_relationship_evidence e
                JOIN historic_relationship_evidence_participants p1
                  ON p1.evidence_id = e.evidence_id
                JOIN historic_relationship_evidence_participants p2
                  ON p2.evidence_id = e.evidence_id
                 AND p1.character_id < p2.character_id
                WHERE e.evidence_date_utc = $import_date_utc
                GROUP BY p1.character_id, p2.character_id
            ) day_pairs;
            """,
            importDateText,
            cancellationToken);

        var totalSummaryRows = await ExecuteScalarLongAsync(
            connection,
            transaction,
            "SELECT COUNT(*) FROM historic_relationship_summary;",
            null,
            cancellationToken);

        return new GroupHistorySummaryBuildResult(
            importDateUtc,
            evidenceRows,
            participantRows,
            pairOccurrenceRows,
            totalSummaryRows);
    }

    private static async Task InsertEvidenceRowAsync(DuckDBConnection connection, System.Data.Common.DbTransaction transaction,
        GroupHistoryEvidenceImportRow row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO historic_relationship_evidence
            (evidence_id, source_killmail_id, killmail_time_utc, evidence_date_utc, solar_system_id,
             participant_count, created_utc)
            VALUES
            ($evidence_id, $source_killmail_id, $killmail_time_utc, $evidence_date_utc, $solar_system_id,
             $participant_count, $created_utc);
            """;
        command.Parameters.Add(new DuckDBParameter("evidence_id", row.KillmailId));
        command.Parameters.Add(new DuckDBParameter("source_killmail_id", row.KillmailId));
        command.Parameters.Add(new DuckDBParameter("killmail_time_utc", row.KillmailTimeUtc.ToString("O")));
        command.Parameters.Add(new DuckDBParameter("evidence_date_utc", row.EvidenceDateUtc.ToString("yyyy-MM-dd")));
        command.Parameters.Add(new DuckDBParameter("solar_system_id", ToDbValue(row.SolarSystemId)));
        command.Parameters.Add(new DuckDBParameter("participant_count", row.ParticipantCount));
        command.Parameters.Add(new DuckDBParameter("created_utc", DateTime.UtcNow.ToString("O")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertParticipantRowAsync(DuckDBConnection connection, System.Data.Common.DbTransaction transaction,
        GroupHistoryParticipantImportRow row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO historic_relationship_evidence_participants
            (evidence_id, character_id, corporation_id, alliance_id, ship_type_id)
            VALUES
            ($evidence_id, $character_id, $corporation_id, $alliance_id, $ship_type_id);
            """;
        command.Parameters.Add(new DuckDBParameter("evidence_id", row.EvidenceId));
        command.Parameters.Add(new DuckDBParameter("character_id", row.CharacterId));
        command.Parameters.Add(new DuckDBParameter("corporation_id", ToDbValue(row.CorporationId)));
        command.Parameters.Add(new DuckDBParameter("alliance_id", ToDbValue(row.AllianceId)));
        command.Parameters.Add(new DuckDBParameter("ship_type_id", ToDbValue(row.ShipTypeId)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkImportDayCompletedCoreAsync(DuckDBConnection connection, System.Data.Common.DbTransaction transaction,
        DateOnly importDateUtc, int rawKillmailCount, int qualifyingKillmailCount, int participantIndexRowCount,
        long pairOccurrenceCount, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO history_import_day_status
                (import_date_utc, status, started_utc, completed_utc, raw_killmail_count,
                 qualifying_killmail_count, participant_index_row_count, pair_occurrence_count, error_message)
                VALUES
                ($import_date_utc, 'Completed', NULL, $completed_utc, $raw_killmail_count,
                 $qualifying_killmail_count, $participant_index_row_count, $pair_occurrence_count, NULL);
                """;
            command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
            command.Parameters.Add(new DuckDBParameter("completed_utc", DateTime.UtcNow.ToString("O")));
            command.Parameters.Add(new DuckDBParameter("raw_killmail_count", rawKillmailCount));
            command.Parameters.Add(new DuckDBParameter("qualifying_killmail_count", qualifyingKillmailCount));
            command.Parameters.Add(new DuckDBParameter("participant_index_row_count", participantIndexRowCount));
            command.Parameters.Add(new DuckDBParameter("pair_occurrence_count", pairOccurrenceCount));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE history_metadata
                SET history_start_day_utc = CASE
                        WHEN history_start_day_utc IS NULL THEN $import_date_utc
                        WHEN history_start_day_utc > $import_date_utc THEN $import_date_utc
                        ELSE history_start_day_utc
                    END,
                    last_completed_day_utc = CASE
                        WHEN last_completed_day_utc IS NULL THEN $import_date_utc
                        WHEN last_completed_day_utc < $import_date_utc THEN $import_date_utc
                        ELSE last_completed_day_utc
                    END,
                    last_update_utc = $last_update_utc,
                    last_import_result = 'Completed'
                WHERE schema_version = $schema_version;
                """;
            command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
            command.Parameters.Add(new DuckDBParameter("last_update_utc", DateTime.UtcNow.ToString("O")));
            command.Parameters.Add(new DuckDBParameter("schema_version", GroupHistoryConstants.SchemaVersion));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private DuckDBConnection CreateConnection()
    {
        var directory = Path.GetDirectoryName(DatabasePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        return new DuckDBConnection($"Data Source={DatabasePath}");
    }

    private static async Task<bool> TableExistsAsync(DuckDBConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = $table_name;";
        command.Parameters.Add(new DuckDBParameter("table_name", tableName));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return ConvertScalarToLong(result) > 0;
    }

    private static async Task<long> ExecuteScalarLongAsync(
        DuckDBConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        string? importDateUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        if (importDateUtc is not null)
            command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return ConvertScalarToLong(result);
    }

    private static async Task ExecuteNonQueryAsync(DuckDBConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int CountDaysInclusive(DateOnly start, DateOnly end)
    {
        return end.DayNumber - start.DayNumber + 1;
    }

    private static object ToDbValue(long? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static long ConvertScalarToLong(object? result)
    {
        return result switch
        {
            null => 0,
            long value => value,
            int value => value,
            System.Numerics.BigInteger value => (long)value,
            _ => Convert.ToInt64(result)
        };
    }

    private static string GetSchemaSql()
    {
        return """
            CREATE TABLE IF NOT EXISTS history_metadata
            (
                schema_version INTEGER NOT NULL,
                history_start_day_utc VARCHAR,
                last_completed_day_utc VARCHAR,
                created_utc VARCHAR NOT NULL,
                last_update_utc VARCHAR,
                average_import_milliseconds_per_day BIGINT,
                last_import_result VARCHAR
            );

            CREATE TABLE IF NOT EXISTS history_import_day_status
            (
                import_date_utc VARCHAR PRIMARY KEY,
                status VARCHAR NOT NULL,
                started_utc VARCHAR,
                completed_utc VARCHAR,
                raw_killmail_count INTEGER NOT NULL,
                qualifying_killmail_count INTEGER NOT NULL,
                participant_index_row_count INTEGER NOT NULL,
                pair_occurrence_count BIGINT NOT NULL,
                error_message VARCHAR
            );

            CREATE TABLE IF NOT EXISTS historic_relationship_evidence
            (
                evidence_id BIGINT PRIMARY KEY,
                source_killmail_id BIGINT NOT NULL UNIQUE,
                killmail_time_utc VARCHAR NOT NULL,
                evidence_date_utc VARCHAR NOT NULL,
                solar_system_id BIGINT,
                participant_count INTEGER NOT NULL,
                created_utc VARCHAR NOT NULL
            );

            CREATE TABLE IF NOT EXISTS historic_relationship_evidence_participants
            (
                evidence_id BIGINT NOT NULL,
                character_id BIGINT NOT NULL,
                corporation_id BIGINT,
                alliance_id BIGINT,
                ship_type_id BIGINT,
                PRIMARY KEY (evidence_id, character_id)
            );

            CREATE TABLE IF NOT EXISTS historic_relationship_summary
            (
                pilot_a_id BIGINT NOT NULL,
                pilot_b_id BIGINT NOT NULL,
                shared_event_count INTEGER NOT NULL,
                first_seen_utc VARCHAR NOT NULL,
                last_seen_utc VARCHAR NOT NULL,
                last_rebuilt_utc VARCHAR NOT NULL,
                PRIMARY KEY (pilot_a_id, pilot_b_id)
            );

            CREATE INDEX IF NOT EXISTS idx_history_import_day_status_status
                ON history_import_day_status(status);

            CREATE INDEX IF NOT EXISTS idx_hids_status_date
                ON history_import_day_status(status, import_date_utc);

            CREATE INDEX IF NOT EXISTS idx_hre_evidence_date
                ON historic_relationship_evidence(evidence_date_utc);

            CREATE INDEX IF NOT EXISTS idx_hre_time
                ON historic_relationship_evidence(killmail_time_utc);

            CREATE INDEX IF NOT EXISTS idx_hrep_character_id
                ON historic_relationship_evidence_participants(character_id);

            CREATE INDEX IF NOT EXISTS idx_hrep_evidence_id
                ON historic_relationship_evidence_participants(evidence_id);

            CREATE INDEX IF NOT EXISTS idx_hrs_pilot_a
                ON historic_relationship_summary(pilot_a_id);

            CREATE INDEX IF NOT EXISTS idx_hrs_pilot_b
                ON historic_relationship_summary(pilot_b_id);
            """;
    }
}