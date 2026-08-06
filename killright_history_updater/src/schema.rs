use duckdb::{params, Connection, Result};

pub const SCHEMA_VERSION: i32 = 1;

pub fn create_schema(connection: &Connection) -> Result<()> {
    connection.execute_batch(SCHEMA_SQL)
}

pub fn insert_initial_metadata_row(connection: &Connection, created_utc: &str) -> Result<()> {
    connection.execute(
        "INSERT INTO history_metadata \
         (schema_version, history_start_day_utc, last_completed_day_utc, created_utc, last_update_utc, \
          average_import_milliseconds_per_day, last_import_result) \
         SELECT ?, NULL, NULL, ?, NULL, NULL, 'Created' \
         WHERE NOT EXISTS (SELECT 1 FROM history_metadata);",
        params![SCHEMA_VERSION, created_utc],
    )?;

    Ok(())
}

/// Drops the secondary indexes on the two tables that `import-day` bulk-loads via
/// the Appender, so the bulk load isn't paying per-row index-maintenance cost.
/// Primary key constraints on both tables are deliberately left in place.
pub fn drop_bulk_load_indexes(connection: &Connection) -> Result<()> {
    connection.execute_batch(
        "DROP INDEX IF EXISTS idx_hre_evidence_date;
         DROP INDEX IF EXISTS idx_hre_time;
         DROP INDEX IF EXISTS idx_hrep_character_id;
         DROP INDEX IF EXISTS idx_hrep_evidence_id;",
    )
}

/// Recreates the indexes dropped by `drop_bulk_load_indexes`, once the bulk load
/// has finished.
pub fn rebuild_bulk_load_indexes(connection: &Connection) -> Result<()> {
    connection.execute_batch(
        "CREATE INDEX IF NOT EXISTS idx_hre_evidence_date
             ON historic_relationship_evidence(evidence_date_utc);
         CREATE INDEX IF NOT EXISTS idx_hre_time
             ON historic_relationship_evidence(killmail_time_utc);
         CREATE INDEX IF NOT EXISTS idx_hrep_character_id
             ON historic_relationship_evidence_participants(character_id);
         CREATE INDEX IF NOT EXISTS idx_hrep_evidence_id
             ON historic_relationship_evidence_participants(evidence_id);",
    )
}

const SCHEMA_SQL: &str = r#"
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

CREATE TABLE IF NOT EXISTS historic_relationship_org_context
(
    pilot_a_id BIGINT NOT NULL,
    pilot_b_id BIGINT NOT NULL,
    shared_event_count_linked INTEGER NOT NULL,
    first_seen_linked_utc VARCHAR,
    last_seen_linked_utc VARCHAR,
    shared_event_count_unlinked INTEGER NOT NULL,
    first_seen_unlinked_utc VARCHAR,
    last_seen_unlinked_utc VARCHAR,
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

CREATE INDEX IF NOT EXISTS idx_hroc_pilot_a
    ON historic_relationship_org_context(pilot_a_id);

CREATE INDEX IF NOT EXISTS idx_hroc_pilot_b
    ON historic_relationship_org_context(pilot_b_id);
"#;

#[cfg(test)]
mod tests {
    use super::*;

    fn count_bulk_load_indexes(connection: &Connection) -> i64 {
        connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_indexes() \
                 WHERE index_name IN ( \
                     'idx_hre_evidence_date', 'idx_hre_time', \
                     'idx_hrep_character_id', 'idx_hrep_evidence_id' \
                 );",
                [],
                |row| row.get(0),
            )
            .unwrap()
    }

    #[test]
    fn drop_bulk_load_indexes_removes_all_four_secondary_indexes() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 4);

        drop_bulk_load_indexes(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 0);
    }

    #[test]
    fn drop_bulk_load_indexes_is_idempotent_when_already_dropped() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        drop_bulk_load_indexes(&connection).unwrap();
        drop_bulk_load_indexes(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 0);
    }

    #[test]
    fn rebuild_bulk_load_indexes_restores_all_four_secondary_indexes() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();
        drop_bulk_load_indexes(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 0);

        rebuild_bulk_load_indexes(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 4);
    }

    #[test]
    fn rebuild_bulk_load_indexes_is_idempotent_when_already_present() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        rebuild_bulk_load_indexes(&connection).unwrap();
        rebuild_bulk_load_indexes(&connection).unwrap();

        assert_eq!(count_bulk_load_indexes(&connection), 4);
    }
}
