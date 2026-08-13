use std::time::Instant;

use chrono::{NaiveDate, Utc};
use duckdb::{params, Connection, Result};

use crate::affiliation_timeline::{apply_daily_affiliation_fragments, build_daily_affiliation_fragments};
use crate::r2_client::{EvidenceDayResult, EvidenceRecord, ParticipantRecord, ZkillHistoryClient};
use crate::schema::SCHEMA_VERSION;

pub struct PersistenceTiming {
    pub clear_existing_rows_elapsed_ms: u128,
    pub evidence_append_elapsed_ms: u128,
    pub participant_append_elapsed_ms: u128,
    pub affiliation_timeline_elapsed_ms: u128,
    pub status_update_elapsed_ms: u128,
    pub total_elapsed_ms: u128,
}

pub struct ImportDayOutcome {
    pub date: NaiveDate,
    pub already_completed: bool,
    pub succeeded: bool,
    pub raw_killmail_count: i32,
    pub qualifying_killmail_count: i32,
    pub persisted_evidence_rows: usize,
    pub persisted_participant_rows: usize,
    pub affiliation_timeline_updated_pilot_count: usize,
    pub candidate_pair_occurrence_rows: i64,
    pub timing: Option<PersistenceTiming>,
    pub error_message: Option<String>,
}

/// Imports a single day's evidence and participant rows.
///
/// No longer manages bulk-load secondary indexes (Step 19.00.53): those
/// indexes are no longer part of the schema at all (see
/// `schema::create_schema`), so there is nothing here to drop before the
/// Appender inserts or rebuild afterwards. Before this guide, this function
/// took a `manage_bulk_load_indexes` bool controlling exactly that; it's gone
/// because both call sites (`staging::build_staging`'s loop and
/// `main::run_import_day`) now behave identically -- there's no longer a
/// meaningful "manage my own indexes" mode to opt into.
///
/// Step 19.01.03: after participant rows are appended, this function also
/// extends/opens `historic_pilot_affiliation_timeline` rows for every pilot
/// observed in this day's participant rows (Design Specification Section
/// 4.7, Implementation Plan Step 19.01.03) -- see
/// `affiliation_timeline::build_daily_affiliation_fragments`/
/// `apply_daily_affiliation_fragments`, wired in via
/// `update_pilot_affiliation_timeline` below. A failure here marks the day
/// Failed, the same as a failed evidence or participant append, rather than
/// leaving the day Completed with a stale or missing timeline update.
pub fn import_day(connection: &Connection, client: &ZkillHistoryClient, date: NaiveDate) -> ImportDayOutcome {
    match is_import_day_completed(connection, date) {
        Ok(true) => return already_completed_outcome(date),
        Ok(false) => {}
        Err(error) => return failed_outcome(date, format!("Failed to check existing import status: {error}")),
    }

    if let Err(error) = mark_import_day_started(connection, date) {
        return failed_outcome(date, format!("Failed to mark import day started: {error}"));
    }

    let extraction = client.extract_day_evidence_with_retry(date);

    if !extraction.day_result.succeeded {
        let error_message = extraction
            .day_result
            .error_message
            .clone()
            .unwrap_or_else(|| "Unknown extraction failure.".to_string());
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }

    let total_start = Instant::now();

    let clear_start = Instant::now();
    if let Err(error) = clear_day_rows(connection, date) {
        let error_message = format!("Failed to clear existing rows before re-import: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }
    let clear_existing_rows_elapsed_ms = clear_start.elapsed().as_millis();

    let evidence_start = Instant::now();
    if let Err(error) = append_evidence_rows(connection, &extraction.evidence_rows) {
        let error_message = format!("Failed to append evidence rows: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }
    let evidence_append_elapsed_ms = evidence_start.elapsed().as_millis();

    let participant_start = Instant::now();
    if let Err(error) = append_participant_rows(connection, &extraction.participant_rows) {
        let error_message = format!("Failed to append participant rows: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }
    let participant_append_elapsed_ms = participant_start.elapsed().as_millis();

    let affiliation_timeline_start = Instant::now();
    let affiliation_timeline_updated_pilot_count = match update_pilot_affiliation_timeline(connection, &extraction.evidence_rows, &extraction.participant_rows) {
        Ok(count) => count,
        Err(error) => {
            let error_message = format!("Failed to update historic_pilot_affiliation_timeline: {error}");
            let _ = mark_import_day_failed(connection, date, &error_message);
            return partial_failure_outcome(date, &extraction, error_message);
        }
    };
    let affiliation_timeline_elapsed_ms = affiliation_timeline_start.elapsed().as_millis();

    let persisted_evidence_rows = extraction.evidence_rows.len();
    let persisted_participant_rows = extraction.participant_rows.len();

    let status_start = Instant::now();
    if let Err(error) = mark_import_day_completed(
        connection,
        date,
        extraction.day_result.raw_killmail_count,
        extraction.day_result.qualifying_killmail_count,
        persisted_participant_rows as i32,
        extraction.day_result.candidate_pair_occurrence_rows,
    ) {
        let error_message = format!("Failed to mark import day completed: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);

        return ImportDayOutcome {
            date,
            already_completed: false,
            succeeded: false,
            raw_killmail_count: extraction.day_result.raw_killmail_count,
            qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
            persisted_evidence_rows,
            persisted_participant_rows,
            affiliation_timeline_updated_pilot_count,
            candidate_pair_occurrence_rows: extraction.day_result.candidate_pair_occurrence_rows,
            timing: None,
            error_message: Some(error_message),
        };
    }
    let status_update_elapsed_ms = status_start.elapsed().as_millis();

    ImportDayOutcome {
        date,
        already_completed: false,
        succeeded: true,
        raw_killmail_count: extraction.day_result.raw_killmail_count,
        qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
        persisted_evidence_rows,
        persisted_participant_rows,
        affiliation_timeline_updated_pilot_count,
        candidate_pair_occurrence_rows: extraction.day_result.candidate_pair_occurrence_rows,
        timing: Some(PersistenceTiming {
            clear_existing_rows_elapsed_ms,
            evidence_append_elapsed_ms,
            participant_append_elapsed_ms,
            affiliation_timeline_elapsed_ms,
            status_update_elapsed_ms,
            total_elapsed_ms: total_start.elapsed().as_millis(),
        }),
        error_message: None,
    }
}

fn already_completed_outcome(date: NaiveDate) -> ImportDayOutcome {
    ImportDayOutcome {
        date,
        already_completed: true,
        succeeded: true,
        raw_killmail_count: 0,
        qualifying_killmail_count: 0,
        persisted_evidence_rows: 0,
        persisted_participant_rows: 0,
        affiliation_timeline_updated_pilot_count: 0,
        candidate_pair_occurrence_rows: 0,
        timing: None,
        error_message: None,
    }
}

fn failed_outcome(date: NaiveDate, error_message: String) -> ImportDayOutcome {
    ImportDayOutcome {
        date,
        already_completed: false,
        succeeded: false,
        raw_killmail_count: 0,
        qualifying_killmail_count: 0,
        persisted_evidence_rows: 0,
        persisted_participant_rows: 0,
        affiliation_timeline_updated_pilot_count: 0,
        candidate_pair_occurrence_rows: 0,
        timing: None,
        error_message: Some(error_message),
    }
}

fn partial_failure_outcome(date: NaiveDate, extraction: &EvidenceDayResult, error_message: String) -> ImportDayOutcome {
    ImportDayOutcome {
        date,
        already_completed: false,
        succeeded: false,
        raw_killmail_count: extraction.day_result.raw_killmail_count,
        qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
        persisted_evidence_rows: 0,
        persisted_participant_rows: 0,
        affiliation_timeline_updated_pilot_count: 0,
        candidate_pair_occurrence_rows: 0,
        timing: None,
        error_message: Some(error_message),
    }
}

fn is_import_day_completed(connection: &Connection, date: NaiveDate) -> Result<bool> {
    let date_text = date.format("%Y-%m-%d").to_string();

    let count: i64 = connection.query_row(
        "SELECT COUNT(*) FROM history_import_day_status WHERE import_date_utc = ? AND status = 'Completed';",
        params![date_text],
        |row| row.get(0),
    )?;

    Ok(count > 0)
}

fn mark_import_day_started(connection: &Connection, date: NaiveDate) -> Result<()> {
    let date_text = date.format("%Y-%m-%d").to_string();
    let started_utc = Utc::now().to_rfc3339();

    connection.execute(
        "INSERT OR REPLACE INTO history_import_day_status \
         (import_date_utc, status, started_utc, completed_utc, raw_killmail_count, \
          qualifying_killmail_count, participant_index_row_count, pair_occurrence_count, error_message) \
         VALUES (?, 'InProgress', ?, NULL, 0, 0, 0, 0, NULL);",
        params![date_text, started_utc],
    )?;

    Ok(())
}

fn mark_import_day_failed(connection: &Connection, date: NaiveDate, error_message: &str) -> Result<()> {
    let date_text = date.format("%Y-%m-%d").to_string();
    let completed_utc = Utc::now().to_rfc3339();

    connection.execute(
        "INSERT OR REPLACE INTO history_import_day_status \
         (import_date_utc, status, started_utc, completed_utc, raw_killmail_count, \
          qualifying_killmail_count, participant_index_row_count, pair_occurrence_count, error_message) \
         VALUES (?, 'Failed', NULL, ?, 0, 0, 0, 0, ?);",
        params![date_text, completed_utc, error_message],
    )?;

    Ok(())
}

fn mark_import_day_completed(
    connection: &Connection,
    date: NaiveDate,
    raw_killmail_count: i32,
    qualifying_killmail_count: i32,
    participant_index_row_count: i32,
    pair_occurrence_count: i64,
) -> Result<()> {
    let date_text = date.format("%Y-%m-%d").to_string();
    let completed_utc = Utc::now().to_rfc3339();

    connection.execute(
        "UPDATE history_import_day_status \
         SET status = 'Completed', completed_utc = ?, raw_killmail_count = ?, \
             qualifying_killmail_count = ?, participant_index_row_count = ?, \
             pair_occurrence_count = ?, error_message = NULL \
         WHERE import_date_utc = ?;",
        params![
            completed_utc,
            raw_killmail_count,
            qualifying_killmail_count,
            participant_index_row_count,
            pair_occurrence_count,
            date_text
        ],
    )?;

    connection.execute(
        "UPDATE history_metadata \
         SET history_start_day_utc = CASE \
                 WHEN history_start_day_utc IS NULL THEN ? \
                 WHEN history_start_day_utc > ? THEN ? \
                 ELSE history_start_day_utc \
             END, \
             last_completed_day_utc = CASE \
                 WHEN last_completed_day_utc IS NULL THEN ? \
                 WHEN last_completed_day_utc < ? THEN ? \
                 ELSE last_completed_day_utc \
             END, \
             last_update_utc = ?, \
             last_import_result = 'Completed' \
         WHERE schema_version = ?;",
        params![
            date_text, date_text, date_text, date_text, date_text, date_text, completed_utc, SCHEMA_VERSION
        ],
    )?;

    Ok(())
}

fn clear_day_rows(connection: &Connection, date: NaiveDate) -> Result<()> {
    let date_text = date.format("%Y-%m-%d").to_string();

    connection.execute(
        "DELETE FROM historic_relationship_evidence_participants \
         WHERE evidence_id IN ( \
             SELECT evidence_id FROM historic_relationship_evidence WHERE evidence_date_utc = ? \
         );",
        params![date_text],
    )?;

    connection.execute(
        "DELETE FROM historic_relationship_evidence WHERE evidence_date_utc = ?;",
        params![date_text],
    )?;

    Ok(())
}

fn append_evidence_rows(connection: &Connection, evidence_rows: &[EvidenceRecord]) -> Result<()> {
    let created_utc = Utc::now().to_rfc3339();
    let mut appender = connection.appender("historic_relationship_evidence")?;

    for row in evidence_rows {
        appender.append_row(params![
            row.killmail_id,
            row.killmail_id,
            row.killmail_time_utc.to_rfc3339(),
            row.evidence_date_utc.format("%Y-%m-%d").to_string(),
            row.solar_system_id,
            row.victim_ship_type_id,
            row.participant_count,
            created_utc.clone(),
        ])?;
    }

    appender.flush()?;
    Ok(())
}

fn append_participant_rows(connection: &Connection, participant_rows: &[ParticipantRecord]) -> Result<()> {
    let mut appender = connection.appender("historic_relationship_evidence_participants")?;

    for row in participant_rows {
        appender.append_row(params![
            row.killmail_id,
            row.character_id,
            row.corporation_id,
            row.alliance_id,
            row.ship_type_id,
        ])?;
    }

    appender.flush()?;
    Ok(())
}

/// Applies Step 19.01.03's incremental historic_pilot_affiliation_timeline
/// maintenance for one imported day (Design Specification Section 4.7):
/// builds this day's per-pilot affiliation fragments from the same evidence
/// and participant rows just appended, then applies them via
/// affiliation_timeline::apply_daily_affiliation_fragments. Returns the
/// number of distinct pilots touched, for import_day's diagnostic report.
fn update_pilot_affiliation_timeline(connection: &Connection, evidence_rows: &[EvidenceRecord], participant_rows: &[ParticipantRecord]) -> Result<usize> {
    let now_utc = Utc::now().to_rfc3339();
    let fragments = build_daily_affiliation_fragments(evidence_rows, participant_rows);

    apply_daily_affiliation_fragments(connection, &fragments, &now_utc)
}

#[cfg(test)]
mod tests {
    use chrono::TimeZone;

    use super::*;

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    fn utc(seconds_of_day: u32) -> chrono::DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 8, 13, 0, 0, 0).unwrap() + chrono::Duration::seconds(seconds_of_day as i64)
    }

    fn evidence(killmail_id: i64, killmail_time_utc: chrono::DateTime<Utc>) -> EvidenceRecord {
        EvidenceRecord {
            killmail_id,
            killmail_time_utc,
            evidence_date_utc: killmail_time_utc.date_naive(),
            solar_system_id: Some(30000142),
            victim_ship_type_id: Some(670),
            participant_count: 1,
        }
    }

    fn participant(killmail_id: i64, character_id: i64, corporation_id: Option<i64>) -> ParticipantRecord {
        ParticipantRecord {
            killmail_id,
            character_id,
            corporation_id,
            alliance_id: None,
            ship_type_id: Some(587),
        }
    }

    /// Step 19.01.03: this is the direct integration test of the wiring
    /// import_day itself relies on -- takes real EvidenceRecord/
    /// ParticipantRecord data (no ZkillHistoryClient/network dependency,
    /// unlike import_day as a whole) and confirms a row lands in
    /// historic_pilot_affiliation_timeline via this crate's own connection,
    /// not just via affiliation_timeline's own unit tests against the pure
    /// functions directly.
    #[test]
    fn update_pilot_affiliation_timeline_inserts_a_row_for_a_newly_observed_pilot() {
        let connection = open_test_schema();
        let evidence_rows = vec![evidence(1, utc(100))];
        let participant_rows = vec![participant(1, 95465499, Some(98765432))];

        let touched = update_pilot_affiliation_timeline(&connection, &evidence_rows, &participant_rows).unwrap();

        assert_eq!(touched, 1);

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline WHERE pilot_id = 95465499;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 1);
    }

    #[test]
    fn update_pilot_affiliation_timeline_extends_across_two_separate_calls() {
        let connection = open_test_schema();

        let day_one_evidence = vec![evidence(1, utc(100))];
        let day_one_participants = vec![participant(1, 95465499, Some(98765432))];
        update_pilot_affiliation_timeline(&connection, &day_one_evidence, &day_one_participants).unwrap();

        let day_two_time = utc(100) + chrono::Duration::days(1);
        let day_two_evidence = vec![evidence(2, day_two_time)];
        let day_two_participants = vec![participant(2, 95465499, Some(98765432))];
        update_pilot_affiliation_timeline(&connection, &day_two_evidence, &day_two_participants).unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline WHERE pilot_id = 95465499;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 1, "two calls with the same corporation_id must extend one row, not open a second");

        let last_seen_utc: String = connection
            .query_row("SELECT last_seen_utc FROM historic_pilot_affiliation_timeline WHERE pilot_id = 95465499;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(last_seen_utc, day_two_time.to_rfc3339());
    }
}
