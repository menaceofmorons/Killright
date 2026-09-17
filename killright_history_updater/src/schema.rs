use duckdb::{params, Connection, Result};

pub const SCHEMA_VERSION: i32 = 1;

pub fn create_schema(connection: &Connection) -> Result<()> {
    connection.execute_batch(SCHEMA_SQL)?;
    drop_unconsumed_secondary_indexes(connection)
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

pub fn drop_unconsumed_secondary_indexes(connection: &Connection) -> Result<()> {
    connection.execute_batch(
        "DROP INDEX IF EXISTS idx_hre_evidence_date;
         DROP INDEX IF EXISTS idx_hre_time;
         DROP INDEX IF EXISTS idx_hrep_character_id;
         DROP INDEX IF EXISTS idx_hrep_evidence_id;
         DROP INDEX IF EXISTS idx_hrs_pilot_a;
         DROP INDEX IF EXISTS idx_hrs_pilot_b;
         DROP INDEX IF EXISTS idx_hroc_pilot_a;
         DROP INDEX IF EXISTS idx_hroc_pilot_b;",
    )
}

pub fn rebuild_unconsumed_secondary_indexes(connection: &Connection) -> Result<()> {
    connection.execute_batch(
        "CREATE INDEX IF NOT EXISTS idx_hre_evidence_date
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
             ON historic_relationship_org_context(pilot_b_id);",
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
    evidence_id BIGINT NOT NULL,
    source_killmail_id BIGINT NOT NULL,
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
    ship_type_id BIGINT
);

CREATE TABLE IF NOT EXISTS historic_relationship_summary
(
    pilot_a_id BIGINT NOT NULL,
    pilot_b_id BIGINT NOT NULL,
    shared_event_count INTEGER NOT NULL,
    first_seen_utc VARCHAR NOT NULL,
    last_seen_utc VARCHAR NOT NULL,
    last_rebuilt_utc VARCHAR NOT NULL
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
    last_rebuilt_utc VARCHAR NOT NULL
);

CREATE TABLE IF NOT EXISTS historic_relationship_classification
(
    pilot_id BIGINT NOT NULL,
    entity_id BIGINT NOT NULL,
    entity_type VARCHAR NOT NULL,
    strength VARCHAR NOT NULL,
    confidence VARCHAR NOT NULL,
    last_computed_utc VARCHAR NOT NULL
);

CREATE TABLE IF NOT EXISTS historic_pilot_affiliation_timeline
(
    pilot_id BIGINT NOT NULL,
    corporation_id BIGINT,
    alliance_id BIGINT,
    first_seen_utc VARCHAR NOT NULL,
    last_seen_utc VARCHAR NOT NULL,
    last_updated_utc VARCHAR NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hpat_pilot_id
    ON historic_pilot_affiliation_timeline(pilot_id);

CREATE TABLE IF NOT EXISTS historic_closed_entity_cache
(
    entity_id BIGINT NOT NULL,
    entity_type VARCHAR NOT NULL,
    discovered_closed_utc VARCHAR NOT NULL,
    PRIMARY KEY (entity_id, entity_type)
);

CREATE INDEX IF NOT EXISTS idx_history_import_day_status_status
    ON history_import_day_status(status);

CREATE INDEX IF NOT EXISTS idx_hids_status_date
    ON history_import_day_status(status, import_date_utc);

"#;

#[cfg(test)]
mod tests {
    use super::*;

    fn count_unconsumed_secondary_indexes(connection: &Connection) -> i64 {
        connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_indexes() \
                 WHERE index_name IN ( \
                     'idx_hre_evidence_date', 'idx_hre_time', \
                     'idx_hrep_character_id', 'idx_hrep_evidence_id', \
                     'idx_hrs_pilot_a', 'idx_hrs_pilot_b', \
                     'idx_hroc_pilot_a', 'idx_hroc_pilot_b' \
                 );",
                [],
                |row| row.get(0),
            )
            .unwrap()
    }

    #[test]
    fn create_schema_does_not_create_unconsumed_secondary_indexes() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 0);
    }

    #[test]
    fn create_schema_drops_unconsumed_secondary_indexes_left_by_a_pre_19_00_53_staging_file() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        rebuild_unconsumed_secondary_indexes(&connection).unwrap();
        assert_eq!(count_unconsumed_secondary_indexes(&connection), 8);

        create_schema(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 0);
    }

    #[test]
    fn drop_unconsumed_secondary_indexes_removes_all_eight_secondary_indexes() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();
        rebuild_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 8);

        drop_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 0);
    }

    #[test]
    fn drop_unconsumed_secondary_indexes_is_idempotent_when_already_dropped() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        drop_unconsumed_secondary_indexes(&connection).unwrap();
        drop_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 0);
    }

    #[test]
    fn rebuild_unconsumed_secondary_indexes_restores_all_eight_secondary_indexes() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();
        drop_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 0);

        rebuild_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 8);
    }

    #[test]
    fn rebuild_unconsumed_secondary_indexes_is_idempotent_when_already_present() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        rebuild_unconsumed_secondary_indexes(&connection).unwrap();
        rebuild_unconsumed_secondary_indexes(&connection).unwrap();

        assert_eq!(count_unconsumed_secondary_indexes(&connection), 8);
    }

    #[test]
    fn create_schema_leaves_no_primary_key_or_unique_constraints_on_the_four_historic_relationship_tables() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let constraint_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_constraints() \
                 WHERE table_name IN ( \
                     'historic_relationship_evidence', \
                     'historic_relationship_evidence_participants', \
                     'historic_relationship_summary', \
                     'historic_relationship_org_context' \
                 ) AND constraint_type IN ('PRIMARY KEY', 'UNIQUE');",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(constraint_count, 0);
    }

    #[test]
    fn create_schema_still_declares_primary_key_on_history_import_day_status() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let constraint_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_constraints() \
                 WHERE table_name = 'history_import_day_status' AND constraint_type = 'PRIMARY KEY';",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(constraint_count, 1);
    }

    #[test]
    fn create_schema_creates_the_three_historic_relationship_classification_tables() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let table_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_tables() \
                 WHERE table_name IN ( \
                     'historic_relationship_classification', \
                     'historic_pilot_affiliation_timeline', \
                     'historic_closed_entity_cache' \
                 );",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(table_count, 3);
    }

    #[test]
    fn create_schema_leaves_no_primary_key_or_unique_constraints_on_historic_relationship_classification_or_historic_pilot_affiliation_timeline(
    ) {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let constraint_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_constraints() \
                 WHERE table_name IN ( \
                     'historic_relationship_classification', \
                     'historic_pilot_affiliation_timeline' \
                 ) AND constraint_type IN ('PRIMARY KEY', 'UNIQUE');",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(constraint_count, 0);
    }

    #[test]
    fn create_schema_declares_primary_key_on_historic_closed_entity_cache() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let constraint_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_constraints() \
                 WHERE table_name = 'historic_closed_entity_cache' AND constraint_type = 'PRIMARY KEY';",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(constraint_count, 1);
    }

    #[test]
    fn historic_relationship_classification_accepts_a_row() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        connection
            .execute_batch(
                "INSERT INTO historic_relationship_classification \
                 (pilot_id, entity_id, entity_type, strength, confidence, last_computed_utc) \
                 VALUES (95465499, 90379338, 'P', 'S', 'High', '2026-08-13T00:00:00Z');",
            )
            .unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_relationship_classification;", [], |row| row.get(0))
            .unwrap();

        assert_eq!(row_count, 1);
    }

    #[test]
    fn historic_pilot_affiliation_timeline_accepts_a_row_with_null_alliance_id() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        connection
            .execute_batch(
                "INSERT INTO historic_pilot_affiliation_timeline \
                 (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
                 VALUES (95465499, 98765432, NULL, '2026-01-01T00:00:00Z', '2026-08-01T00:00:00Z', '2026-08-13T00:00:00Z');",
            )
            .unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();

        assert_eq!(row_count, 1);
    }

    #[test]
    fn historic_closed_entity_cache_accepts_a_row() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        connection
            .execute_batch(
                "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) \
                 VALUES (99005338, 'C', '2026-08-13T00:00:00Z');",
            )
            .unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_closed_entity_cache;", [], |row| row.get(0))
            .unwrap();

        assert_eq!(row_count, 1);
    }

    #[test]
    fn historic_closed_entity_cache_rejects_a_duplicate_entity_id_and_entity_type() {
        let connection = Connection::open_in_memory().unwrap();
        create_schema(&connection).unwrap();

        connection
            .execute_batch(
                "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) \
                 VALUES (99005338, 'C', '2026-08-13T00:00:00Z');",
            )
            .unwrap();

        let result = connection.execute_batch(
            "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) \
             VALUES (99005338, 'C', '2026-08-14T00:00:00Z');",
        );

        assert!(result.is_err());
    }

    #[test]
    fn create_schema_creates_pilot_id_index_on_historic_pilot_affiliation_timeline() {
        let connection = Connection::open_in_memory().unwrap();

        create_schema(&connection).unwrap();

        let index_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM duckdb_indexes() WHERE index_name = 'idx_hpat_pilot_id';",
                [],
                |row| row.get(0),
            )
            .unwrap();

        assert_eq!(index_count, 1);
    }
}
