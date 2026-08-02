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
        {
            return new GroupHistoryDatabaseStatus(
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var schemaExists = await TableExistsAsync(connection, "history_metadata", cancellationToken);

        if (!schemaExists)
        {
            return new GroupHistoryDatabaseStatus(
                true,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                schema_version,
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
        {
            return new GroupHistoryDatabaseStatus(true, true, null, null, null, null, null, null, null);
        }

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
            (
                schema_version,
                history_start_day_utc,
                last_completed_day_utc,
                created_utc,
                last_update_utc,
                average_import_milliseconds_per_day,
                last_import_result
            )
            SELECT
                $schema_version,
                NULL,
                NULL,
                $created_utc,
                NULL,
                NULL,
                'Created'
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
            var estimatedInitialDuration = TimeSpan.FromMinutes(40);
            return new GroupHistoryUpdateRequirement(
                status.DatabaseExists,
                status.SchemaExists,
                true,
                latestImportableDay.AddYears(-GroupHistoryConstants.InitialHistoricImportHorizonYears),
                latestImportableDay,
                GroupHistoryConstants.InitialHistoricImportHorizonYears * 365,
                estimatedInitialDuration,
                true,
                "Historic Group Detection database has not been created.");
        }

        if (status.LastCompletedDayUtc is null)
        {
            var firstImportDay = latestImportableDay.AddYears(-GroupHistoryConstants.InitialHistoricImportHorizonYears);
            return new GroupHistoryUpdateRequirement(
                true,
                true,
                true,
                firstImportDay,
                latestImportableDay,
                CountDaysInclusive(firstImportDay, latestImportableDay),
                TimeSpan.FromMinutes(40),
                true,
                "Historic Group Detection database is empty.");
        }

        if (status.LastCompletedDayUtc.Value >= latestImportableDay)
        {
            return new GroupHistoryUpdateRequirement(
                true,
                true,
                false,
                null,
                null,
                0,
                TimeSpan.Zero,
                false,
                "Historic Group Detection database is current.");
        }

        var firstMissingDay = status.LastCompletedDayUtc.Value.AddDays(1);
        var missingDayCount = CountDaysInclusive(firstMissingDay, latestImportableDay);
        var millisecondsPerDay = status.AverageImportMillisecondsPerDay ?? 500;
        var estimatedDuration = TimeSpan.FromMilliseconds(millisecondsPerDay * missingDayCount);
        var promptRequired = estimatedDuration > TimeSpan.FromMinutes(GroupHistoryConstants.PromptThresholdMinutes);

        return new GroupHistoryUpdateRequirement(
            true,
            true,
            false,
            firstMissingDay,
            latestImportableDay,
            missingDayCount,
            estimatedDuration,
            promptRequired,
            $"Historic Group Detection database is missing {missingDayCount} completed UTC day(s).");
    }

    public async Task MarkImportDayStartedAsync(DateOnly importDateUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO history_import_day_status
            (
                import_date_utc,
                status,
                started_utc,
                completed_utc,
                raw_killmail_count,
                qualifying_killmail_count,
                participant_index_row_count,
                pair_occurrence_count,
                error_message
            )
            VALUES
            (
                $import_date_utc,
                'InProgress',
                $started_utc,
                NULL,
                0,
                0,
                0,
                0,
                NULL
            );
            """;
        command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
        command.Parameters.Add(new DuckDBParameter("started_utc", DateTime.UtcNow.ToString("O")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkImportDayCompletedAsync(
        DateOnly importDateUtc,
        int rawKillmailCount,
        int qualifyingKillmailCount,
        int participantIndexRowCount,
        long pairOccurrenceCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE history_import_day_status
                SET
                    status = 'Completed',
                    completed_utc = $completed_utc,
                    raw_killmail_count = $raw_killmail_count,
                    qualifying_killmail_count = $qualifying_killmail_count,
                    participant_index_row_count = $participant_index_row_count,
                    pair_occurrence_count = $pair_occurrence_count,
                    error_message = NULL
                WHERE import_date_utc = $import_date_utc;
                """;
            command.Parameters.Add(new DuckDBParameter("completed_utc", DateTime.UtcNow.ToString("O")));
            command.Parameters.Add(new DuckDBParameter("raw_killmail_count", rawKillmailCount));
            command.Parameters.Add(new DuckDBParameter("qualifying_killmail_count", qualifyingKillmailCount));
            command.Parameters.Add(new DuckDBParameter("participant_index_row_count", participantIndexRowCount));
            command.Parameters.Add(new DuckDBParameter("pair_occurrence_count", pairOccurrenceCount));
            command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE history_metadata
                SET
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

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkImportDayFailedAsync(DateOnly importDateUtc, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO history_import_day_status
            (
                import_date_utc,
                status,
                started_utc,
                completed_utc,
                raw_killmail_count,
                qualifying_killmail_count,
                participant_index_row_count,
                pair_occurrence_count,
                error_message
            )
            VALUES
            (
                $import_date_utc,
                'Failed',
                NULL,
                $completed_utc,
                0,
                0,
                0,
                0,
                $error_message
            );
            """;
        command.Parameters.Add(new DuckDBParameter("import_date_utc", importDateUtc.ToString("yyyy-MM-dd")));
        command.Parameters.Add(new DuckDBParameter("completed_utc", DateTime.UtcNow.ToString("O")));
        command.Parameters.Add(new DuckDBParameter("error_message", errorMessage));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_name = $table_name;
            """;
        command.Parameters.Add(new DuckDBParameter("table_name", tableName));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) > 0;
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
                victim_character_id BIGINT,
                victim_corporation_id BIGINT,
                victim_alliance_id BIGINT,
                victim_ship_type_id BIGINT,
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
                final_blow BOOLEAN,
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

            CREATE INDEX IF NOT EXISTS idx_hre_evidence_date
                ON historic_relationship_evidence(evidence_date_utc);

            CREATE INDEX IF NOT EXISTS idx_hrep_character_id
                ON historic_relationship_evidence_participants(character_id);

            CREATE INDEX IF NOT EXISTS idx_hrs_pilot_a
                ON historic_relationship_summary(pilot_a_id);

            CREATE INDEX IF NOT EXISTS idx_hrs_pilot_b
                ON historic_relationship_summary(pilot_b_id);
            """;
    }
}