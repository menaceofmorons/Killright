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
///
/// Step 19.00.55: `SCHEMA_SQL` below also no longer declares an inline
/// `PRIMARY KEY`/`UNIQUE` constraint on any of the same four tables (Design
/// Specification v5.4 Section 6.9.2: "The working (updater) database carries
/// no primary keys or indexes of any kind while it is being built or
/// updated"). This finishes what 19.00.53 started for secondary indexes --
/// there is nothing further for `create_schema` to drop here, since a
/// primary key constraint cannot be added to a `CREATE TABLE IF NOT EXISTS`
/// that already exists without first dropping and recreating the table, and
/// no staging file built before this guide is carried forward across it (see
/// 19.00.54's Clean Slate Reset). `history_metadata` and
/// `history_import_day_status` are unaffected: Section 4.7 documents no
/// promotion-time primary key for either, and `history_import_day_status`'s
/// own `import_date_utc VARCHAR PRIMARY KEY` is working-database bookkeeping
/// this crate's own `determine_missing_days`/`persistence::import_day`
/// actively rely on every run, not evidence data subject to promotion-time
/// deduplication.
///
/// Step 19.01.01: `SCHEMA_SQL` gains three new tables for Historic
/// Relationship Classification (Design Specification Section 6.11):
/// `historic_relationship_classification` (Section 4.8), and
/// `historic_pilot_affiliation_timeline`/`historic_closed_entity_cache`
/// (both Section 4.7, new in this step). `historic_relationship_classification`
/// and `historic_pilot_affiliation_timeline` follow the same no-inline-PK
/// convention as the four `historic_relationship_*` tables above: promoting
/// either to Live is explicitly deferred beyond the whole 19.01.** step
/// family (`KillRight-Historic-Relationship-Classification-Implementation-Plan-19.01.md`),
/// so there is no promotion-time constraint step to defer to yet.
/// `historic_closed_entity_cache` is Working-side only by design -- it never
/// reaches a promotion step at all -- so it declares its `PRIMARY KEY`
/// inline instead, matching `history_import_day_status` above for the same
/// reason. `SCHEMA_VERSION` is not bumped by this step, matching how every
/// prior additive schema change in this file (including the
/// `historic_relationship_org_context` table and 19.00.55's primary-key
/// removal) has been handled.
///
/// Step 19.01.03: `SCHEMA_SQL` gains one secondary index, `idx_hpat_pilot_id`
/// on `historic_pilot_affiliation_timeline(pilot_id)`. Unlike the eight
/// secondary indexes `drop_unconsumed_secondary_indexes` removes above, this
/// one is added deliberately rather than deferred: Step 19.01.03 is the
/// first code in this crate to actually query
/// `historic_pilot_affiliation_timeline` by `pilot_id` (once per distinct
/// pilot observed in each imported day, from
/// `persistence::update_pilot_affiliation_timeline`), matching this file's
/// own stated policy of adding an index only once a real read pattern needs
/// it (see `drop_unconsumed_secondary_indexes`'s doc comment).
/// `SCHEMA_VERSION` is still not bumped -- an added index changes no data
/// shape, matching how `idx_history_import_day_status_status`/
/// `idx_hids_status_date` below were also added without a version bump.
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
/// Step 19.00.55: primary key constraints on the same four tables are no
/// longer declared in `SCHEMA_SQL` at all (see `create_schema`'s doc
/// comment) -- there is no equivalent "drop the primary key" step here,
/// since none is ever created for a working database built by this crate
/// going forward.
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

-- Step 19.00.55: no PRIMARY KEY on evidence_id, no UNIQUE on
-- source_killmail_id. Design Specification v5.4 Section 4.7 documents both
-- as primary/uniqueness constraints established only on the promoted
-- Live-folder copy (19.00.56), never on this working table. See
-- create_schema's doc comment.
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

-- Step 19.00.55: no composite PRIMARY KEY (evidence_id, character_id).
-- Documented by Section 4.7 as established only on the promoted copy.
CREATE TABLE IF NOT EXISTS historic_relationship_evidence_participants
(
    evidence_id BIGINT NOT NULL,
    character_id BIGINT NOT NULL,
    corporation_id BIGINT,
    alliance_id BIGINT,
    ship_type_id BIGINT
);

-- Step 19.00.55: no composite PRIMARY KEY (pilot_a_id, pilot_b_id).
-- Documented by Section 4.7 as established only on the promoted copy.
CREATE TABLE IF NOT EXISTS historic_relationship_summary
(
    pilot_a_id BIGINT NOT NULL,
    pilot_b_id BIGINT NOT NULL,
    shared_event_count INTEGER NOT NULL,
    first_seen_utc VARCHAR NOT NULL,
    last_seen_utc VARCHAR NOT NULL,
    last_rebuilt_utc VARCHAR NOT NULL
);

-- Step 19.00.55: no composite PRIMARY KEY (pilot_a_id, pilot_b_id).
-- Documented by Section 4.7 as established only on the promoted copy.
-- summary_rebuild.rs's DELETE-then-INSERT rebuild pattern (not an upsert)
-- does not depend on this table having a primary key.
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

-- Step 19.01.01: historic_relationship_classification (Design Specification
-- Section 4.8) -- the persisted output of Historic Relationship
-- Classification (Section 6.11). No inline PRIMARY KEY, matching the same
-- working-database convention as the four historic_relationship_* tables
-- above (Section 6.9.2/19.00.55): promoting this table to Live is explicitly
-- deferred beyond the whole 19.01.** step family, so there is no
-- promotion-time constraint step to defer to yet. entity_type is one of
-- 'P' (pilot), 'C' (corporation), or 'A' (alliance); strength is one of
-- 'VS', 'S', 'M', 'W' (None is never stored); confidence is one of 'Low',
-- 'Average', 'High'. Value validation is application-level, not enforced by
-- this schema (matching how no other VARCHAR-enum column in this file is
-- constrained by a CHECK).
CREATE TABLE IF NOT EXISTS historic_relationship_classification
(
    pilot_id BIGINT NOT NULL,
    entity_id BIGINT NOT NULL,
    entity_type VARCHAR NOT NULL,
    strength VARCHAR NOT NULL,
    confidence VARCHAR NOT NULL,
    last_computed_utc VARCHAR NOT NULL
);

-- Step 19.01.01: historic_pilot_affiliation_timeline (Design Specification
-- Section 4.7) -- a pilot's own corporation/alliance affiliation history
-- over time, built incrementally from historic_relationship_evidence_participants
-- above as each day is imported (Section 6.9), not from ESI. Scope is
-- therefore bounded to what appears in qualifying killmail evidence -- see
-- Section 6.11 Known Limitations. No inline PRIMARY KEY, same reasoning as
-- historic_relationship_classification above -- Live promotion is deferred
-- beyond this step family.
CREATE TABLE IF NOT EXISTS historic_pilot_affiliation_timeline
(
    pilot_id BIGINT NOT NULL,
    corporation_id BIGINT,
    alliance_id BIGINT,
    first_seen_utc VARCHAR NOT NULL,
    last_seen_utc VARCHAR NOT NULL,
    last_updated_utc VARCHAR NOT NULL
);

-- Step 19.01.03: pilot_id lookup index for historic_pilot_affiliation_timeline
-- (Implementation Plan Step 19.01.03) -- added deliberately, unlike the eight
-- secondary indexes removed above, because this step is the first real read
-- pattern against this table (persistence::update_pilot_affiliation_timeline
-- looks up each distinct pilot's most-recently-seen row once per imported
-- day). See create_schema's doc comment.
CREATE INDEX IF NOT EXISTS idx_hpat_pilot_id
    ON historic_pilot_affiliation_timeline(pilot_id);

-- Step 19.01.01: historic_closed_entity_cache (Design Specification Section
-- 4.7) -- append-only cache of corporation/alliance IDs discovered closed
-- via ESI, backing the Active Entity Short-Circuit (Section 6.11.4).
-- Working-side only by design -- it never reaches a promotion step at all --
-- so, unlike the two tables above, its uniqueness constraint is declared
-- inline here rather than deferred. Matches history_import_day_status's
-- inline PRIMARY KEY above, for the same reason. entity_type is one of 'C'
-- (corporation) or 'A' (alliance).
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

    /// Step 19.00.55: the direct test of this guide's schema change --
    /// queries DuckDB's own `duckdb_constraints()` system table function
    /// (the same introspection style `duckdb_indexes()` already used above)
    /// to confirm none of the four historic_relationship_* tables carries a
    /// PRIMARY KEY or UNIQUE constraint after create_schema runs.
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

    /// Step 19.00.55: guards the other side of the same change -- confirms
    /// this guide did not accidentally also strip history_import_day_status's
    /// primary key, which determine_missing_days and persistence::import_day
    /// depend on every run and which Section 4.7 does not document as a
    /// promotion-time-only constraint.
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

    /// Step 19.01.01: confirms all three new Historic Relationship
    /// Classification tables exist after create_schema runs, using the same
    /// duckdb_tables() introspection style as duckdb_indexes()/
    /// duckdb_constraints() above.
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

    /// Step 19.01.01: historic_relationship_classification and
    /// historic_pilot_affiliation_timeline follow the same no-inline-PK
    /// working-database convention as the four historic_relationship_*
    /// tables (see the four-table test above) -- Live promotion for either
    /// is deferred beyond this whole step family.
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

    /// Step 19.01.01: historic_closed_entity_cache is Working-side only by
    /// design and never reaches a promotion step, so its uniqueness
    /// constraint is declared inline and must exist immediately after
    /// create_schema runs -- unlike the two tables in the test above.
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

    /// Step 19.01.03: confirms idx_hpat_pilot_id exists after create_schema
    /// runs, using the same duckdb_indexes() introspection style as
    /// count_unconsumed_secondary_indexes above -- but, unlike the eight
    /// secondary indexes that helper counts, this one is expected to exist,
    /// not be absent.
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
