use std::time::Instant;

use chrono::Utc;
use duckdb::{params, Connection, Result as DuckResult};

use crate::confidence::Confidence;
use crate::episode_builder::NPC_CORPORATION_ID_THRESHOLD;
use crate::esi_active_status::{EntityType, EsiActiveStatusClient};
use crate::pilot_to_pilot_strength::{classify_pilot_to_pilot_strength, Strength};
use crate::pilot_vs_group_strength::{classify_pilot_vs_group_strength_with_active_entity_short_circuit, PilotVsGroupOutcome};

const STAGING_TABLE_NAME: &str = "historic_relationship_classification_staging";
const PREVIOUS_TABLE_NAME: &str = "historic_relationship_classification_previous";
const LIVE_TABLE_NAME: &str = "historic_relationship_classification";

/// One positive row bound for historic_relationship_classification
/// (Design_Spec_Dense.md §4.8) -- entity_type is always "P", "C", or "A".
/// Strength::None is never represented here: every producer of this struct
/// (classify_p2p_positives, classify_pvg_positives) filters it out first
/// (§6.11.2 "Store positives only").
pub struct ClassificationRow {
    pub pilot_id: i64,
    pub entity_id: i64,
    pub entity_type: &'static str,
    pub strength: &'static str,
    pub confidence: &'static str,
    pub last_computed_utc: String,
}

pub struct PersistClassificationTiming {
    pub p2p_classify_elapsed_ms: u128,
    pub pvg_classify_elapsed_ms: u128,
    pub live_swap_elapsed_ms: u128,
    pub total_elapsed_ms: u128,
}

pub struct PersistClassificationOutcome {
    pub p2p_candidate_count: usize,
    pub pvg_candidate_count: usize,
    pub positive_row_count: usize,
    pub timing: PersistClassificationTiming,
}

/// Maps a positive Strength to its schema code (Design_Spec_Dense.md §4.8:
/// 'VS'/'S'/'M'/'W', None never stored). Callers always filter out
/// Strength::None before calling this -- see ClassificationRow's own doc
/// comment -- so that arm is unreachable rather than a silently-wrong empty
/// string.
fn strength_code(strength: Strength) -> &'static str {
    match strength {
        Strength::None => unreachable!("Strength::None is never stored -- callers filter it out before strength_code is called"),
        Strength::Weak => "W",
        Strength::Medium => "M",
        Strength::Strong => "S",
        Strength::VeryStrong => "VS",
    }
}

/// Maps Confidence to its schema code (Design_Spec_Dense.md §4.8:
/// 'Low'/'Average'/'High') -- matches Confidence's own Display output
/// exactly, but returns a non-allocating &'static str rather than requiring
/// callers to heap-allocate via .to_string().
fn confidence_code(confidence: Confidence) -> &'static str {
    match confidence {
        Confidence::Low => "Low",
        Confidence::Average => "Average",
        Confidence::High => "High",
    }
}

/// Step 19.01.09 candidate source for Entity Type P: every pilot pair with
/// at least one shared-evidence event ever. Design_Spec_Dense.md §6.11.2's
/// positives-only persistence means only pairs already showing some
/// relationship signal are worth evaluating -- an unbounded scan of every
/// possible pilot pair in EVE is not tractable, and classify_pilot_to_pilot_strength's
/// own "never same c/a, ever" pattern otherwise returns Strong even for two
/// pilots with zero shared evidence (see pilot_to_pilot_strength.rs's own
/// classify_pilot_to_pilot_strength_strong_when_never_same_c_a test).
/// historic_relationship_summary already stores pilot_a_id/pilot_b_id in the
/// same lower-character-ID-first canonical ordering Design_Spec_Dense.md §4.7
/// documents for both it and historic_relationship_classification's own
/// pilot_id column (§6.11.2), so no re-ordering is needed here.
pub fn fetch_p2p_candidate_pairs(connection: &Connection) -> DuckResult<Vec<(i64, i64)>> {
    let mut statement = connection.prepare("SELECT DISTINCT pilot_a_id, pilot_b_id FROM historic_relationship_summary;")?;
    let rows = statement.query_map([], |row| Ok((row.get::<_, i64>(0)?, row.get::<_, i64>(1)?)))?;

    let mut pairs = Vec::new();
    for row in rows {
        pairs.push(row?);
    }

    Ok(pairs)
}

/// Step 19.01.09 candidate source for Entity Type C/A: every (pilot, entity)
/// combination where the entity appears in that pilot's own
/// historic_pilot_affiliation_timeline. Design_Spec_Dense.md §6.11.4: "no
/// membership to anchor a row to" means an entity the pilot never belonged
/// to is unstored by definition, so it is excluded here rather than
/// evaluated and discarded. NPC corporations (episode_builder::NPC_CORPORATION_ID_THRESHOLD)
/// are also excluded up front: is_same_c_a always returns false for an NPC
/// corporation (even against itself), so classify_pilot_vs_group_strength
/// always finds zero episodes and returns None for one -- excluding them
/// here skips that always-None computation, and its Active Entity
/// Short-Circuit ESI call, entirely rather than computing and discarding it.
pub fn fetch_pvg_candidates(connection: &Connection) -> DuckResult<Vec<(i64, EntityType, i64)>> {
    let mut candidates = Vec::new();

    let mut corp_statement = connection.prepare(
        "SELECT DISTINCT pilot_id, corporation_id FROM historic_pilot_affiliation_timeline \
         WHERE corporation_id IS NOT NULL AND corporation_id >= ?;",
    )?;
    let corp_rows = corp_statement.query_map(params![NPC_CORPORATION_ID_THRESHOLD], |row| Ok((row.get::<_, i64>(0)?, row.get::<_, i64>(1)?)))?;
    for row in corp_rows {
        let (pilot_id, corporation_id) = row?;
        candidates.push((pilot_id, EntityType::Corporation, corporation_id));
    }

    let mut alliance_statement =
        connection.prepare("SELECT DISTINCT pilot_id, alliance_id FROM historic_pilot_affiliation_timeline WHERE alliance_id IS NOT NULL;")?;
    let alliance_rows = alliance_statement.query_map([], |row| Ok((row.get::<_, i64>(0)?, row.get::<_, i64>(1)?)))?;
    for row in alliance_rows {
        let (pilot_id, alliance_id) = row?;
        candidates.push((pilot_id, EntityType::Alliance, alliance_id));
    }

    Ok(candidates)
}

/// Classifies every Entity Type P candidate pair against `connection` (the
/// Working database -- Design_Spec_Dense.md §6.9.2's always-current source
/// of evidence, distinct from the Live-folder copy this step's persistence
/// pass writes to -- see this module's own doc comment on the Persistence
/// decision above swap_in_live_classification_table), returning only
/// positives: Strength::None is never stored (§6.11.2).
pub fn classify_p2p_positives(connection: &Connection, pairs: &[(i64, i64)], now_utc: &str) -> DuckResult<Vec<ClassificationRow>> {
    let mut rows = Vec::new();

    for &(pilot_a_id, pilot_b_id) in pairs {
        if let Some((strength, confidence)) = classify_pilot_to_pilot_strength(connection, pilot_a_id, pilot_b_id)? {
            if strength != Strength::None {
                rows.push(ClassificationRow {
                    pilot_id: pilot_a_id,
                    entity_id: pilot_b_id,
                    entity_type: "P",
                    strength: strength_code(strength),
                    confidence: confidence_code(confidence),
                    last_computed_utc: now_utc.to_string(),
                });
            }
        }
    }

    Ok(rows)
}

/// Classifies every Entity Type C/A candidate against `connection` (the
/// Working database), applying the Active Entity Short-Circuit (Step
/// 19.01.08) via `esi_client` for each -- a closed entity is Skipped, not
/// evaluated, matching classify_pilot_vs_group_strength_with_active_entity_short_circuit's
/// own contract. An individual ESI call error aborts the whole pass
/// (propagated via `?`) -- there is no partial-success persistence model for
/// this bulk pass, matching how run_print_pilot_vs_group_strength already
/// treats a single classification error as fatal.
pub fn classify_pvg_positives(
    connection: &Connection,
    esi_client: &EsiActiveStatusClient,
    candidates: &[(i64, EntityType, i64)],
    now_utc: &str,
) -> Result<Vec<ClassificationRow>, String> {
    let mut rows = Vec::new();

    for &(pilot_id, entity_type, entity_id) in candidates {
        let outcome =
            classify_pilot_vs_group_strength_with_active_entity_short_circuit(connection, esi_client, pilot_id, entity_type, entity_id, now_utc)?;

        if let PilotVsGroupOutcome::Classified(Some((strength, confidence))) = outcome {
            if strength != Strength::None {
                rows.push(ClassificationRow {
                    pilot_id,
                    entity_id,
                    entity_type: entity_type.as_cache_char(),
                    strength: strength_code(strength),
                    confidence: confidence_code(confidence),
                    last_computed_utc: now_utc.to_string(),
                });
            }
        }
    }

    Ok(rows)
}

/// Writes `rows` into `connection`'s own historic_relationship_classification
/// table via a single-table swap, not an in-place upsert (this module's own
/// Persistence decision, confirmed with Tom 2026-09-14): builds a fresh copy
/// of the table under a temporary name, appends every row, then atomically
/// renames it into place. This keeps readers from ever observing a
/// partially-written table, and a crash mid-append leaves the
/// previously-current table completely untouched.
///
/// No uniqueness check runs here, deliberately (confirmed with Tom
/// 2026-09-14, against the risk of a full-table scan/index-build cost on a
/// table expected to reach millions of rows on every single run): a
/// duplicate (pilot_id, entity_id, entity_type) is not reachable through
/// this pipeline's own logic, so a runtime check would only ever be
/// defending against a future regression, at a cost that scales with the
/// live table's full size on every run regardless. `fetch_p2p_candidate_pairs`/
/// `fetch_pvg_candidates` each already deduplicate their own candidates via
/// `SELECT DISTINCT`, `classify_p2p_positives`/`classify_pvg_positives` each
/// visit every candidate exactly once, canonical pilot ordering for Entity
/// Type P is structural (baked into summary_rebuild.rs's `p1.character_id <
/// p2.character_id` join, not a convention that could drift), and P/C/A rows
/// can never collide on entity_type alone. A DuckDB `CREATE UNIQUE INDEX` on
/// the staging table was tried and rejected here for an unrelated, purely
/// mechanical reason too: DuckDB refuses `ALTER TABLE ... RENAME` on a table
/// with a dependent index ("Dependency Error: Cannot alter entry ... because
/// there are entries that depend on it"), which would have forced dropping
/// the index again before every rename regardless of the cost question
/// above. This module's own tests
/// (fetch_p2p_candidate_pairs_returns_distinct_summary_pairs,
/// fetch_pvg_candidates_*, classify_p2p_positives_*) are the guard against
/// that upstream invariant breaking, not a runtime constraint here.
///
/// `connection` must already have gone through schema::create_schema (the
/// caller's responsibility, matching every other write path in this crate),
/// so LIVE_TABLE_NAME is guaranteed to already exist and a plain ALTER TABLE
/// RENAME is sufficient -- no `IF EXISTS` branching is needed. No
/// end-to-end row-shape validation is performed either: every row is
/// constructed by strength_code/confidence_code's own exhaustive matches
/// over closed enums, so an out-of-range strength/confidence value cannot
/// reach this function to begin with. Mirrors summary_rebuild.rs's own
/// full-rebuild convention (DELETE/INSERT there, swap here) rather than
/// incremental per-pilot maintenance -- this pass always reflects the
/// source database's current truth in full.
pub fn swap_in_live_classification_table(connection: &Connection, rows: &[ClassificationRow]) -> Result<(), String> {
    connection
        .execute_batch(&format!(
            "DROP TABLE IF EXISTS {STAGING_TABLE_NAME}; \
             CREATE TABLE {STAGING_TABLE_NAME} ( \
                 pilot_id BIGINT NOT NULL, \
                 entity_id BIGINT NOT NULL, \
                 entity_type VARCHAR NOT NULL, \
                 strength VARCHAR NOT NULL, \
                 confidence VARCHAR NOT NULL, \
                 last_computed_utc VARCHAR NOT NULL \
             );"
        ))
        .map_err(|error| format!("Failed to create the staging classification table: {error}"))?;

    {
        let mut appender = connection
            .appender(STAGING_TABLE_NAME)
            .map_err(|error| format!("Failed to open an appender on the staging classification table: {error}"))?;

        for row in rows {
            appender
                .append_row(params![row.pilot_id, row.entity_id, row.entity_type, row.strength, row.confidence, row.last_computed_utc])
                .map_err(|error| format!("Failed to append a classification row to the staging table: {error}"))?;
        }

        appender.flush().map_err(|error| format!("Failed to flush the staging classification appender: {error}"))?;
    }

    connection
        .execute_batch(&format!(
            "DROP TABLE IF EXISTS {PREVIOUS_TABLE_NAME}; \
             ALTER TABLE {LIVE_TABLE_NAME} RENAME TO {PREVIOUS_TABLE_NAME}; \
             ALTER TABLE {STAGING_TABLE_NAME} RENAME TO {LIVE_TABLE_NAME}; \
             DROP TABLE IF EXISTS {PREVIOUS_TABLE_NAME};"
        ))
        .map_err(|error| format!("Failed to swap the staging classification table into place: {error}"))?;

    connection
        .execute_batch("CHECKPOINT;")
        .map_err(|error| format!("Failed to checkpoint the database after the classification swap: {error}"))
}

/// Step 19.01.09 entry point: classifies every Entity Type P/C/A candidate
/// against `connection`, then bulk-persists the positives back into that
/// same database via `swap_in_live_classification_table`. One connection,
/// one database, for both the read and the write side -- the original
/// design here opened a separate `working_connection` (evidence source) and
/// `live_connection` (write target) against two different files, which in
/// practice meant reading from whichever ad-hoc scratch database happened to
/// be at `database_path::get_default_database_path()` while writing into the
/// real promoted Live-folder database; corrected same day (confirmed with
/// Tom 2026-09-14) after that mismatch surfaced during this step's own
/// Developer verification. `database_path::get_default_database_path()` now
/// points at a dedicated `Test` subfolder (`folder_layout::test_dir`,
/// `database_path.rs`) precisely so day-to-day runs -- including this step's
/// own Developer verification -- operate against a small scratch database,
/// never the multi-GB real Live-folder one. Running this against the real
/// Live-folder database at all is deliberately deferred to the end of the
/// whole 19.01.** phase, once every step in it has passed against the Test
/// database (confirmed with Tom 2026-09-14) -- not wired into
/// staging::build_staging either way; when/how a Live-folder run is invoked
/// is a decision for that later point, not this step.
pub fn persist_classification(connection: &Connection, esi_client: &EsiActiveStatusClient) -> Result<PersistClassificationOutcome, String> {
    let total_start = Instant::now();
    let now_utc = Utc::now().to_rfc3339();

    let p2p_start = Instant::now();
    let p2p_pairs = fetch_p2p_candidate_pairs(connection).map_err(|error| format!("Failed to fetch Pilot-to-Pilot candidate pairs: {error}"))?;
    let mut positive_rows =
        classify_p2p_positives(connection, &p2p_pairs, &now_utc).map_err(|error| format!("Failed to classify Pilot-to-Pilot candidates: {error}"))?;
    let p2p_classify_elapsed_ms = p2p_start.elapsed().as_millis();

    let pvg_start = Instant::now();
    let pvg_candidates = fetch_pvg_candidates(connection).map_err(|error| format!("Failed to fetch Pilot-vs-Group candidates: {error}"))?;
    let pvg_rows = classify_pvg_positives(connection, esi_client, &pvg_candidates, &now_utc)?;
    positive_rows.extend(pvg_rows);
    let pvg_classify_elapsed_ms = pvg_start.elapsed().as_millis();

    let live_swap_start = Instant::now();
    swap_in_live_classification_table(connection, &positive_rows)?;
    let live_swap_elapsed_ms = live_swap_start.elapsed().as_millis();

    Ok(PersistClassificationOutcome {
        p2p_candidate_count: p2p_pairs.len(),
        pvg_candidate_count: pvg_candidates.len(),
        positive_row_count: positive_rows.len(),
        timing: PersistClassificationTiming {
            p2p_classify_elapsed_ms,
            pvg_classify_elapsed_ms,
            live_swap_elapsed_ms,
            total_elapsed_ms: total_start.elapsed().as_millis(),
        },
    })
}

#[cfg(test)]
mod tests {
    use chrono::{DateTime, Duration, Utc};

    use crate::schema;

    use super::*;

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        schema::create_schema(&connection).unwrap();
        connection
    }

    fn ago(days: i64) -> DateTime<Utc> {
        Utc::now() - Duration::days(days)
    }

    fn insert_affiliation_segment(connection: &Connection, pilot_id: i64, corporation_id: Option<i64>, alliance_id: Option<i64>, first_seen_utc: DateTime<Utc>, last_seen_utc: DateTime<Utc>) {
        connection
            .execute(
                "INSERT INTO historic_pilot_affiliation_timeline \
                 (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
                 VALUES (?, ?, ?, ?, ?, ?);",
                params![pilot_id, corporation_id, alliance_id, first_seen_utc.to_rfc3339(), last_seen_utc.to_rfc3339(), last_seen_utc.to_rfc3339()],
            )
            .unwrap();
    }

    fn insert_summary_row(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64) {
        connection
            .execute(
                "INSERT INTO historic_relationship_summary \
                 (pilot_a_id, pilot_b_id, shared_event_count, first_seen_utc, last_seen_utc, last_rebuilt_utc) \
                 VALUES (?, ?, 1, ?, ?, ?);",
                params![pilot_a_id, pilot_b_id, ago(1).to_rfc3339(), ago(1).to_rfc3339(), ago(1).to_rfc3339()],
            )
            .unwrap();
    }

    const PILOT_A: i64 = 95465499;
    const PILOT_B: i64 = 90379338;
    const GROUP_CORP: i64 = 90333333;
    const NPC_CORP: i64 = 1000001;

    #[test]
    fn strength_code_matches_schema_convention() {
        assert_eq!(strength_code(Strength::Weak), "W");
        assert_eq!(strength_code(Strength::Medium), "M");
        assert_eq!(strength_code(Strength::Strong), "S");
        assert_eq!(strength_code(Strength::VeryStrong), "VS");
    }

    #[test]
    fn confidence_code_matches_schema_convention() {
        assert_eq!(confidence_code(Confidence::Low), "Low");
        assert_eq!(confidence_code(Confidence::Average), "Average");
        assert_eq!(confidence_code(Confidence::High), "High");
    }

    #[test]
    fn fetch_p2p_candidate_pairs_returns_distinct_summary_pairs() {
        let connection = open_test_schema();
        insert_summary_row(&connection, PILOT_A, PILOT_B);

        let pairs = fetch_p2p_candidate_pairs(&connection).unwrap();

        assert_eq!(pairs, vec![(PILOT_A, PILOT_B)]);
    }

    #[test]
    fn fetch_p2p_candidate_pairs_empty_when_no_summary_rows_exist() {
        let connection = open_test_schema();

        assert!(fetch_p2p_candidate_pairs(&connection).unwrap().is_empty());
    }

    #[test]
    fn fetch_pvg_candidates_includes_a_real_corporation_and_excludes_an_npc_corporation() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(GROUP_CORP), None, ago(400), ago(300));
        insert_affiliation_segment(&connection, PILOT_A, Some(NPC_CORP), None, ago(300), ago(1));

        let candidates = fetch_pvg_candidates(&connection).unwrap();

        assert!(candidates.contains(&(PILOT_A, EntityType::Corporation, GROUP_CORP)));
        assert!(!candidates.iter().any(|candidate| candidate.2 == NPC_CORP), "NPC corporation must be excluded from candidate enumeration");
    }

    #[test]
    fn fetch_pvg_candidates_includes_an_alliance() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(GROUP_CORP), Some(99005338), ago(400), ago(1));

        let candidates = fetch_pvg_candidates(&connection).unwrap();

        assert!(candidates.contains(&(PILOT_A, EntityType::Alliance, 99005338)));
    }

    #[test]
    fn classify_p2p_positives_persists_the_never_same_c_a_strong_pattern() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(90111111), None, ago(400), ago(1));
        insert_affiliation_segment(&connection, PILOT_B, Some(90222222), None, ago(400), ago(1));

        let rows = classify_p2p_positives(&connection, &[(PILOT_A, PILOT_B)], "2026-09-14T00:00:00Z").unwrap();

        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].pilot_id, PILOT_A);
        assert_eq!(rows[0].entity_id, PILOT_B);
        assert_eq!(rows[0].entity_type, "P");
        assert_eq!(rows[0].strength, "S");
    }

    #[test]
    fn classify_p2p_positives_omits_a_none_strength_result() {
        let connection = open_test_schema();
        // Two episodes with no between-episode evidence collapse and decay to
        // None (pilot_to_pilot_strength.rs's own
        // classify_pilot_to_pilot_strength_collapses_two_episodes_without_between_episode_evidence_and_decays).
        insert_affiliation_segment(&connection, PILOT_B, Some(90333333), None, ago(1040), ago(1000));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT_A, Some(90555555), None, ago(1010), ago(1000));

        let rows = classify_p2p_positives(&connection, &[(PILOT_A, PILOT_B)], "2026-09-14T00:00:00Z").unwrap();

        assert!(rows.is_empty(), "Strength::None must never be persisted");
    }

    #[test]
    fn classify_p2p_positives_omits_a_currently_same_c_a_pair() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(90111111), None, ago(400), ago(1));
        insert_affiliation_segment(&connection, PILOT_B, Some(90111111), None, ago(400), ago(1));

        let rows = classify_p2p_positives(&connection, &[(PILOT_A, PILOT_B)], "2026-09-14T00:00:00Z").unwrap();

        assert!(rows.is_empty());
    }

    #[test]
    fn classify_pvg_positives_skips_a_cached_closed_entity_and_produces_no_row() {
        let connection = open_test_schema();
        connection
            .execute(
                "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) VALUES (?, 'C', ?);",
                params![GROUP_CORP, "2026-08-01T00:00:00Z"],
            )
            .unwrap();
        let esi_client = EsiActiveStatusClient::new().unwrap();

        let rows = classify_pvg_positives(&connection, &esi_client, &[(PILOT_A, EntityType::Corporation, GROUP_CORP)], "2026-09-14T00:00:00Z").unwrap();

        assert!(rows.is_empty(), "a closed entity must be skipped, not classified and discarded");
    }

    fn sample_row(pilot_id: i64, entity_id: i64) -> ClassificationRow {
        ClassificationRow {
            pilot_id,
            entity_id,
            entity_type: "P",
            strength: "S",
            confidence: "Low",
            last_computed_utc: "2026-09-14T00:00:00Z".to_string(),
        }
    }

    #[test]
    fn swap_in_live_classification_table_persists_the_given_rows() {
        let live_connection = open_test_schema();

        swap_in_live_classification_table(&live_connection, &[sample_row(PILOT_A, PILOT_B)]).unwrap();

        let row_count: i64 = live_connection.query_row("SELECT COUNT(*) FROM historic_relationship_classification;", [], |row| row.get(0)).unwrap();
        assert_eq!(row_count, 1);
    }

    #[test]
    fn swap_in_live_classification_table_replaces_rather_than_appends_on_a_second_run() {
        let live_connection = open_test_schema();

        swap_in_live_classification_table(&live_connection, &[sample_row(PILOT_A, PILOT_B)]).unwrap();
        swap_in_live_classification_table(&live_connection, &[sample_row(PILOT_A, PILOT_B)]).unwrap();

        let row_count: i64 = live_connection.query_row("SELECT COUNT(*) FROM historic_relationship_classification;", [], |row| row.get(0)).unwrap();
        assert_eq!(row_count, 1, "a second run must replace the table, not double its rows");
    }

    #[test]
    fn swap_in_live_classification_table_with_no_positive_rows_leaves_an_empty_table() {
        let live_connection = open_test_schema();

        swap_in_live_classification_table(&live_connection, &[]).unwrap();

        let row_count: i64 = live_connection.query_row("SELECT COUNT(*) FROM historic_relationship_classification;", [], |row| row.get(0)).unwrap();
        assert_eq!(row_count, 0);
    }
}
