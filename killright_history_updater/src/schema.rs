use duckdb::{params, Connection, Result};

pub const SCHEMA_VERSION: i32 = 1;

/// Ensures the historic schema exists, then unconditionally drops the eight
/// currently-unconsumed secondary indexes (Step 19.00.53): the four bulk-load
/// indexes on `historic_relationship_evidence`/`historic_relationship_evidence_participants`,
/// and the four summary/org-context indexes on `historic_relationship_summary`/
/// `historic_relationship_org_context`. None of these are part of `SCHEMA_SQL`
/// any more -- nothing in `killright_engine` (the only live/interactive query
/// surface) reads any `historic_relationship_*` table yet, so maintaining any
/// of these cost real time on every build for zero current benefit. Calling
/// `drop_unconsumed_secondary_indexes` here, rather than just no longer
/// creating them going forward, means a staging file built before this guide
/// sheds all eight automatically the next time it's opened -- no Wipe/Reset
/// required. See `drop_unconsumed_secondary_indexes`'s own doc comment for
/// what to consider before reintroducing any of these once 19.02 (Historic
/// Analysis) needs them.
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

/// Drops the eight currently-unconsumed secondary indexes: the four bulk-load
/// indexes on `historic_relationship_evidence`/`historic_relationship_evidence_participants`
/// (originally written at 19.00.46, batch-scoped at 19.00.52), plus the four
/// summary/org-context indexes on `historic_relationship_summary`/
/// `historic_relationship_org_context` (present since 19.00.46's initial
/// schema, never previously dropped or rebuilt around anything -- they were
/// simply always live). As of 19.00.53 this is no longer called from any
/// bulk-load or summary-rebuild path -- `create_schema` calls it
/// unconditionally instead, purely as a one-time migration for staging files
/// that still carry any of these indexes from before this guide. Left in
/// place, still unit tested below, as the starting point for whenever 19.02
/// (Historic Analysis) defines an actual read pattern that needs indexing --
/// read the deferred partitioned-archive idea recorded against 19.02 in
/// `KillRight-Documentation-Tracker.md` before reintroducing a
/// drop/rebuild-per-build approach as-is, since that cost scaling with total
/// table size is exactly what this guide removed.
/// Primary key constraints on all four tables are, as before, left untouched.
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

/// Recreates the eight indexes dropped by `drop_unconsumed_secondary_indexes`.
/// No longer called anywhere in the current pipeline (19.00.53) -- kept, and
/// still unit tested below, as the starting point for whenever a real
/// consumer needs any of these indexes back.
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

-- Step 19.00.53: idx_hre_evidence_date, idx_hre_time, idx_hrep_character_id,
-- idx_hrep_evidence_id, idx_hrs_pilot_a, idx_hrs_pilot_b, idx_hroc_pilot_a,
-- and idx_hroc_pilot_b were all removed from here. Nothing queries any
-- historic_relationship_* table by these columns yet -- killright_engine has
-- zero references to any of them. Building and maintaining these indexes
-- cost real time on every staging build for zero benefit. See this file's
-- drop_unconsumed_secondary_indexes and rebuild_unconsumed_secondary_indexes
-- doc comments before reintroducing any of them.
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

        // Simulates a staging file built before Step 19.00.53, which still
        // has these indexes from schema creation back then.
        rebuild_unconsumed_secondary_indexes(&connection).unwrap();
        assert_eq!(count_unconsumed_secondary_indexes(&connection), 8);

        // The next time this staging file is opened, create_schema runs again
        // (every CLI command and build_staging call it unconditionally) and
        // should shed them.
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
}
