use std::collections::HashSet;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use chrono::{Duration, Months, NaiveDate, Utc};
use duckdb::{params, Connection, Result as DuckResult};

use crate::persistence::{self, ImportDayOutcome};
use crate::r2_client::ZkillHistoryClient;
use crate::schema::{self, SCHEMA_VERSION};
use crate::summary_rebuild::{self, RebuildStats};

pub const LATEST_VALIDATED_BUILD_MARKER_FILE_NAME: &str = "latest_validated_build.txt";
pub const DEFAULT_INITIAL_HISTORIC_IMPORT_HORIZON_YEARS: u32 = 10;

pub struct ValidationFailure {
    pub check_name: &'static str,
    pub detail: String,
}

pub struct StagingBuildOutcome {
    pub staging_file_name: String,
    pub staging_file_path: PathBuf,
    pub copied_from: Option<String>,
    pub requested_days: Vec<NaiveDate>,
    pub imported_days: Vec<ImportDayOutcome>,
    pub rebuild_stats: RebuildStats,
    pub validation_failures: Vec<ValidationFailure>,
    pub succeeded: bool,
}

pub fn build_staging(
    directory: &Path,
    client: &ZkillHistoryClient,
    horizon_days_override: Option<i64>,
) -> Result<StagingBuildOutcome, String> {
    fs::create_dir_all(directory).map_err(|error| format!("Failed to create staging directory: {error}"))?;

    let copy_basis = find_copy_basis(directory);
    let (staging_path, staging_file_name) = allocate_new_staging_filename(directory)
        .map_err(|error| format!("Failed to allocate staging filename: {error}"))?;

    let copied_from = match &copy_basis {
        Some(source_path) => {
            fs::copy(source_path, &staging_path).map_err(|error| format!("Failed to copy staging file: {error}"))?;
            source_path.file_name().map(|name| name.to_string_lossy().to_string())
        }
        None => None,
    };

    let connection =
        Connection::open(&staging_path).map_err(|error| format!("Failed to open staging database: {error}"))?;

    schema::create_schema(&connection).map_err(|error| format!("Failed to ensure schema: {error}"))?;
    let created_utc = Utc::now().to_rfc3339();
    schema::insert_initial_metadata_row(&connection, &created_utc)
        .map_err(|error| format!("Failed to ensure initial metadata row: {error}"))?;

    let utc_today = Utc::now().date_naive();
    let requested_days = determine_missing_days(&connection, horizon_days_override, utc_today)
        .map_err(|error| format!("Failed to determine missing days: {error}"))?;

    let mut imported_days = Vec::with_capacity(requested_days.len());

    for date in &requested_days {
        imported_days.push(persistence::import_day(&connection, client, *date));
    }

    let rebuild_stats = summary_rebuild::rebuild_summary_and_org_context(&connection)
        .map_err(|error| format!("Failed to rebuild summary and org context: {error}"))?;

    let validation_failures = validate_staging_build(connection, &staging_path, &requested_days)
        .map_err(|error| format!("Failed to run validation checks: {error}"))?;

    let succeeded = validation_failures.is_empty();

    if succeeded {
        write_latest_validated_build_marker(directory, &staging_file_name)
            .map_err(|error| format!("Failed to update the latest-validated-build marker: {error}"))?;
    }

    Ok(StagingBuildOutcome {
        staging_file_name,
        staging_file_path: staging_path,
        copied_from,
        requested_days,
        imported_days,
        rebuild_stats,
        validation_failures,
        succeeded,
    })
}

fn allocate_new_staging_filename(directory: &Path) -> io::Result<(PathBuf, String)> {
    let today = Utc::now().date_naive();
    let date_prefix = today.format("%y%m%d").to_string();
    let mut sequence: u32 = 1;

    loop {
        let filename = format!("KillRight.History.{date_prefix}.{sequence:02}.duckdb");
        let candidate = directory.join(&filename);

        if !candidate.exists() {
            return Ok((candidate, filename));
        }

        sequence += 1;
    }
}

fn find_copy_basis(directory: &Path) -> Option<PathBuf> {
    let marker_path = directory.join(LATEST_VALIDATED_BUILD_MARKER_FILE_NAME);
    let filename = fs::read_to_string(&marker_path).ok()?;
    let filename = filename.trim();

    if filename.is_empty() {
        return None;
    }

    let candidate = directory.join(filename);

    if candidate.exists() {
        Some(candidate)
    } else {
        None
    }
}

fn write_latest_validated_build_marker(directory: &Path, filename: &str) -> io::Result<()> {
    let marker_path = directory.join(LATEST_VALIDATED_BUILD_MARKER_FILE_NAME);
    fs::write(marker_path, filename)
}

fn determine_missing_days(
    connection: &Connection,
    horizon_days_override: Option<i64>,
    utc_today: NaiveDate,
) -> DuckResult<Vec<NaiveDate>> {
    let latest_importable_day = utc_today.pred_opt().expect("date underflow computing latest importable day");

    let horizon_start = match horizon_days_override {
        Some(days) => latest_importable_day - Duration::days((days - 1).max(0)),
        None => latest_importable_day
            .checked_sub_months(Months::new(12 * DEFAULT_INITIAL_HISTORIC_IMPORT_HORIZON_YEARS))
            .expect("date underflow computing default historic horizon"),
    };

    let horizon_start_text = horizon_start.format("%Y-%m-%d").to_string();
    let latest_importable_day_text = latest_importable_day.format("%Y-%m-%d").to_string();

    let mut statement = connection.prepare(
        "SELECT import_date_utc FROM history_import_day_status \
         WHERE status = 'Completed' AND import_date_utc >= ? AND import_date_utc <= ?;",
    )?;

    let mut completed_days: HashSet<NaiveDate> = HashSet::new();
    let mut rows = statement.query(params![horizon_start_text, latest_importable_day_text])?;

    while let Some(row) = rows.next()? {
        let text: String = row.get(0)?;

        if let Ok(date) = NaiveDate::parse_from_str(&text, "%Y-%m-%d") {
            completed_days.insert(date);
        }
    }

    let mut missing_days = Vec::new();
    let mut current = horizon_start;

    while current <= latest_importable_day {
        if !completed_days.contains(&current) {
            missing_days.push(current);
        }

        current = current.succ_opt().expect("date overflow while building horizon range");
    }

    Ok(missing_days)
}

fn validate_staging_build(
    connection: Connection,
    database_path: &Path,
    requested_days: &[NaiveDate],
) -> DuckResult<Vec<ValidationFailure>> {
    let mut failures = Vec::new();

    if !requested_days.is_empty() {
        let start_text = requested_days.first().unwrap().format("%Y-%m-%d").to_string();
        let end_text = requested_days.last().unwrap().format("%Y-%m-%d").to_string();

        let incomplete_count: i64 = connection.query_row(
            "SELECT COUNT(*) FROM history_import_day_status \
             WHERE import_date_utc >= ? AND import_date_utc <= ? AND status IN ('Failed', 'InProgress');",
            params![start_text, end_text],
            |row| row.get(0),
        )?;

        if incomplete_count > 0 {
            failures.push(ValidationFailure {
                check_name: "no_failed_or_in_progress_days",
                detail: format!("{incomplete_count} day(s) in the requested range are Failed or InProgress."),
            });
        }
    }

    let schema_version: i32 =
        connection.query_row("SELECT schema_version FROM history_metadata LIMIT 1;", [], |row| row.get(0))?;

    if schema_version != SCHEMA_VERSION {
        failures.push(ValidationFailure {
            check_name: "schema_version_matches",
            detail: format!("history_metadata.schema_version is {schema_version}, expected {SCHEMA_VERSION}."),
        });
    }

    let mut samples = Vec::new();

    // Scoped so the prepared statement and its row cursor are finalized here,
    // before CHECKPOINT and drop(connection) below. Left un-dropped, they
    // only finalize at this function's end -- after the connection is already
    // closed and the clean-reopen check has already run -- which keeps
    // DuckDB's underlying file handle open on this process for its entire
    // remaining lifetime. See Revision note (REV-D).
    {
        let mut statement = connection
            .prepare("SELECT pilot_a_id, pilot_b_id, shared_event_count FROM historic_relationship_summary LIMIT 5;")?;
        let mut sample_rows = statement.query([])?;

        while let Some(row) = sample_rows.next()? {
            samples.push((row.get::<_, i64>(0)?, row.get::<_, i64>(1)?, row.get::<_, i64>(2)?));
        }
    }

    for (pilot_a_id, pilot_b_id, shared_event_count) in samples {
        let recount: i64 = connection.query_row(
            "SELECT COUNT(*) \
             FROM historic_relationship_evidence e \
             JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id AND p1.character_id = ? \
             JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id AND p2.character_id = ?;",
            params![pilot_a_id, pilot_b_id],
            |row| row.get(0),
        )?;

        if recount != shared_event_count {
            failures.push(ValidationFailure {
                check_name: "sample_pair_recount",
                detail: format!(
                    "Pair ({pilot_a_id}, {pilot_b_id}): summary shared_event_count={shared_event_count}, recount={recount}."
                ),
            });
        }
    }

    let checkpoint_result = connection.execute_batch("CHECKPOINT;");

    // The connection must be fully closed before we can prove the file re-opens cleanly:
    // DuckDB refuses a second connection to the same file while this process still holds
    // one open, so the file handle has to be released first.
    drop(connection);

    match checkpoint_result {
        Err(error) => {
            failures.push(ValidationFailure {
                check_name: "checkpoint",
                detail: format!("CHECKPOINT failed: {error}"),
            });
        }
        Ok(()) => {
            if let Err(error) = reopen_cleanly(database_path) {
                failures.push(ValidationFailure {
                    check_name: "clean_reopen",
                    detail: format!(
                        "Re-opening the staging file from a separate process after CHECKPOINT failed: {error}"
                    ),
                });
            }
        }
    }

    Ok(failures)
}

/// Proves `database_path` opens cleanly by asking a brand-new, separate OS
/// process to open it — this same binary, re-invoked with the internal-only
/// `__verify-open <path>` command — rather than reopening within this process.
///
/// A same-process reopen immediately after `drop(connection)` reliably failed
/// (REV-A, REV-B), including after a full second of retries, always reporting
/// this process's own PID as the file's holder. A genuinely separate process
/// (confirmed with Python's `duckdb` package during diagnosis) opened the
/// identical, still-on-disk file immediately once this process had dropped its
/// connection. That rules out an OS-level lock or a release-timing race: the
/// lock is internal to this process, not the file. Asking a fresh child
/// process to do the opening is also a more faithful test of what this check
/// was always meant to prove — that the finished file opens cleanly for
/// whoever opens it next.
fn reopen_cleanly(database_path: &Path) -> Result<(), String> {
    let executable_path = std::env::current_exe()
        .map_err(|error| format!("Failed to resolve the current executable path: {error}"))?;

    let output = std::process::Command::new(executable_path)
        .arg("__verify-open")
        .arg(database_path)
        .output()
        .map_err(|error| format!("Failed to launch the verification process: {error}"))?;

    if output.status.success() {
        Ok(())
    } else {
        let stderr = String::from_utf8_lossy(&output.stderr);
        Err(format!("Verification process exited with {}: {}", output.status, stderr.trim()))
    }
}
