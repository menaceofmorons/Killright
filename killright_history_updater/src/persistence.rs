use chrono::{NaiveDate, Utc};
use duckdb::{params, Connection, Result};

use crate::r2_client::{EvidenceRecord, ParticipantRecord, ZkillHistoryClient};
use crate::schema::SCHEMA_VERSION;

pub struct ImportDayOutcome {
    pub date: NaiveDate,
    pub already_completed: bool,
    pub succeeded: bool,
    pub raw_killmail_count: i32,
    pub qualifying_killmail_count: i32,
    pub persisted_evidence_rows: usize,
    pub persisted_participant_rows: usize,
    pub candidate_pair_occurrence_rows: i64,
    pub error_message: Option<String>,
}

pub fn import_day(connection: &Connection, client: &ZkillHistoryClient, date: NaiveDate) -> ImportDayOutcome {
    match is_import_day_completed(connection, date) {
        Ok(true) => return already_completed_outcome(date),
        Ok(false) => {}
        Err(error) => return failed_outcome(date, format!("Failed to check existing import status: {error}")),
    }

    if let Err(error) = mark_import_day_started(connection, date) {
        return failed_outcome(date, format!("Failed to mark import day started: {error}"));
    }

    let extraction = client.extract_day_evidence(date, None);

    if !extraction.day_result.succeeded {
        let error_message = extraction
            .day_result
            .error_message
            .clone()
            .unwrap_or_else(|| "Unknown extraction failure.".to_string());
        let _ = mark_import_day_failed(connection, date, &error_message);

        return ImportDayOutcome {
            date,
            already_completed: false,
            succeeded: false,
            raw_killmail_count: extraction.day_result.raw_killmail_count,
            qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
            persisted_evidence_rows: 0,
            persisted_participant_rows: 0,
            candidate_pair_occurrence_rows: 0,
            error_message: Some(error_message),
        };
    }

    if let Err(error) = clear_day_rows(connection, date) {
        let error_message = format!("Failed to clear existing rows before re-import: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }

    if let Err(error) = persist_day_rows(connection, &extraction.evidence_rows, &extraction.participant_rows) {
        let error_message = format!("Failed to persist evidence/participant rows: {error}");
        let _ = mark_import_day_failed(connection, date, &error_message);
        return partial_failure_outcome(date, &extraction, error_message);
    }

    let persisted_evidence_rows = extraction.evidence_rows.len();
    let persisted_participant_rows = extraction.participant_rows.len();

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
            candidate_pair_occurrence_rows: extraction.day_result.candidate_pair_occurrence_rows,
            error_message: Some(error_message),
        };
    }

    ImportDayOutcome {
        date,
        already_completed: false,
        succeeded: true,
        raw_killmail_count: extraction.day_result.raw_killmail_count,
        qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
        persisted_evidence_rows,
        persisted_participant_rows,
        candidate_pair_occurrence_rows: extraction.day_result.candidate_pair_occurrence_rows,
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
        candidate_pair_occurrence_rows: 0,
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
        candidate_pair_occurrence_rows: 0,
        error_message: Some(error_message),
    }
}

fn partial_failure_outcome(
    date: NaiveDate,
    extraction: &crate::r2_client::EvidenceDayResult,
    error_message: String,
) -> ImportDayOutcome {
    ImportDayOutcome {
        date,
        already_completed: false,
        succeeded: false,
        raw_killmail_count: extraction.day_result.raw_killmail_count,
        qualifying_killmail_count: extraction.day_result.qualifying_killmail_count,
        persisted_evidence_rows: 0,
        persisted_participant_rows: 0,
        candidate_pair_occurrence_rows: 0,
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

fn persist_day_rows(
    connection: &Connection,
    evidence_rows: &[EvidenceRecord],
    participant_rows: &[ParticipantRecord],
) -> Result<()> {
    let created_utc = Utc::now().to_rfc3339();

    {
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
    }

    {
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
    }

    Ok(())
}
