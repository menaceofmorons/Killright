use std::collections::HashSet;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

use chrono::{Duration, Months, NaiveDate, Utc};
use duckdb::{params, Connection, Result as DuckResult};

use crate::live_config::{self, GroupHistoryLiveConfig};
use crate::folder_layout;
use crate::persistence::{self, ImportDayOutcome};
use crate::r2_client::ZkillHistoryClient;
use crate::schema::{self, SCHEMA_VERSION};
use crate::summary_rebuild::{self, RebuildStats};

pub const LATEST_VALIDATED_BUILD_MARKER_FILE_NAME: &str = "latest_validated_build.txt";
pub const DEFAULT_INITIAL_HISTORIC_IMPORT_HORIZON_YEARS: u32 = 10;

/// Suffix used for the per-day progress log written alongside a staging file
/// while its day-import loop runs (Step CC-07.08.26.01). Paired 1:1 with the
/// staging file by replacing its `.duckdb` extension with this suffix, so it
/// shares the same base name -- e.g. `KillRight.History.260807.02.duckdb`'s
/// progress log is `KillRight.History.260807.02.progress.log`. Deliberately
/// distinct from the C# project's `HistoryUpdaterStagingPaths.StagingFileSearchPattern`
/// (`KillRight.History.*.duckdb`) so the existing orphaned-staging-file
/// cleanup never matches or deletes it.
pub const PROGRESS_LOG_FILE_SUFFIX: &str = ".progress.log";

pub const TEST_MODE_ANCHOR_YEAR: i32 = 2016;
pub const TEST_MODE_ANCHOR_MONTH: u32 = 8;
pub const TEST_MODE_ANCHOR_DAY: u32 = 1;

/// The fixed starting point used only by the Test Amount control (Step 19.00.51).
/// Never used for a default (blank) build-staging launch, and never derived from
/// the current date -- the same amount requested on different days therefore
/// always resolves to the same [start, end] range.
pub fn test_mode_anchor_start_date() -> NaiveDate {
    NaiveDate::from_ymd_opt(TEST_MODE_ANCHOR_YEAR, TEST_MODE_ANCHOR_MONTH, TEST_MODE_ANCHOR_DAY)
        .expect("test-mode anchor start date must be a valid calendar date")
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TestAmountUnit {
    Days,
    Months,
    Years,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ImportRangeMode {
    DefaultTenYearLookback,
    HorizonDaysBackFromToday(i64),
    AnchoredTestAmount(i64, TestAmountUnit),
}

/// Finds the latest day already `Completed` at or after the anchor, or the day
/// before the anchor if nothing in the test range has been imported yet. This is
/// the basis Test Amount extends from, so successive runs are additive (a Month
/// run after a completed Week run extends from day 7, not from the anchor again).
///
/// Only considers days at or after `anchor_start`: a database that has also been
/// used for a default/production run (whose ~10-year lookback can itself reach
/// back close to the fixed 01 Aug 2016 anchor, depending on when it ran) could
/// otherwise report a frontier far beyond any test-amount run actually made.
/// Wipe (Section 3.10) exists to give a clean baseline before a test cycle for
/// exactly this reason.
fn find_test_range_frontier(connection: &Connection, anchor_start: NaiveDate) -> DuckResult<NaiveDate> {
    let anchor_start_text = anchor_start.format("%Y-%m-%d").to_string();

    let max_completed: Option<String> = connection.query_row(
        "SELECT MAX(import_date_utc) FROM history_import_day_status \
         WHERE status = 'Completed' AND import_date_utc >= ?;",
        params![anchor_start_text],
        |row| row.get(0),
    )?;

    let anchor_predecessor = anchor_start.pred_opt().expect("date underflow computing anchor predecessor");

    Ok(match max_completed {
        Some(text) => NaiveDate::parse_from_str(&text, "%Y-%m-%d").unwrap_or(anchor_predecessor),
        None => anchor_predecessor,
    })
}

/// Computes the inclusive [start, end] range for a Test Amount request: start is
/// always the fixed anchor date; end is the current frontier (see
/// `find_test_range_frontier`) plus the requested amount, clamped so it can never
/// exceed `latest_importable_day` (a defensive guard for an implausibly large
/// amount -- not expected to trigger for any realistic timing-test value, since
/// the anchor is a decade in the past).
fn compute_anchored_test_range(
    connection: &Connection,
    amount: i64,
    unit: TestAmountUnit,
    latest_importable_day: NaiveDate,
) -> DuckResult<(NaiveDate, NaiveDate)> {
    let start = test_mode_anchor_start_date();
    let frontier = find_test_range_frontier(connection, start)?;

    let unclamped_end = match unit {
        TestAmountUnit::Days => frontier + Duration::days(amount.max(0)),
        TestAmountUnit::Months => frontier
            .checked_add_months(Months::new(amount.max(0) as u32))
            .expect("date overflow computing test-mode amount end date"),
        TestAmountUnit::Years => frontier
            .checked_add_months(Months::new((amount.max(0) as u32).saturating_mul(12)))
            .expect("date overflow computing test-mode amount end date"),
    };

    let end = unclamped_end.min(latest_importable_day);

    Ok((start, end))
}

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
    range_mode: ImportRangeMode,
) -> Result<StagingBuildOutcome, String> {
    // Step 19.00.55: `directory` is the historic-database root (e.g.
    // %LOCALAPPDATA%\KillRight\HistoryUpdater); the working database this
    // function builds now lives in its `Working` subfolder, alongside the
    // sibling `Live`/`Archive`/`Failed` subfolders later steps write to
    // (Design Specification v5.4 Section 6.9.2/4.7; the plan's 19.00.55
    // section).
    folder_layout::ensure_folder_layout(directory)
        .map_err(|error| format!("Failed to create historic database folder layout: {error}"))?;
    let working_directory = folder_layout::working_dir(directory);

    let live_config_directory = live_config::get_live_config_directory();
    let pre_build_live_config = live_config::read_live_config(&live_config_directory);

    live_config::write_live_config(
        &live_config_directory,
        &GroupHistoryLiveConfig {
            update_in_progress: true,
            update_in_progress_pid: Some(std::process::id()),
            ..pre_build_live_config.clone()
        },
    )
    .map_err(|error| format!("Failed to write groupHistory live config: {error}"))?;

    let copy_basis = find_copy_basis(&working_directory);
    let (staging_path, staging_file_name) = allocate_new_working_filename(&working_directory)
        .map_err(|error| format!("Failed to allocate working filename: {error}"))?;

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
    let requested_days = determine_missing_days(&connection, range_mode, utc_today)
        .map_err(|error| format!("Failed to determine missing days: {error}"))?;

    let mut imported_days = Vec::with_capacity(requested_days.len());

    // Step CC-07.08.26.01: a plain-text progress log, one line before and one
    // line after each day's import, opened before the loop and written via a
    // raw, unbuffered File so every line is durable the instant it's written.
    // Only created when there is at least one day to import, mirroring the
    // same requested_days.is_empty() guard 19.00.52/19.00.53 used for index
    // management. This is the only record of exactly which day a run was on
    // if the process is killed outright (a Halt from the Developer window's
    // Stop button) -- nothing about a killed process's in-memory state
    // survives, and stdout from a detached launch
    // (HistoryUpdaterProcessLauncher.LaunchDetached) is never captured or
    // shown anywhere. Deleted below once a build succeeds; kept when a build
    // fails validation, alongside the staging file itself, for diagnosis.
    let progress_log_path = if requested_days.is_empty() {
        None
    } else {
        Some(progress_log_path_for(&working_directory, &staging_file_name))
    };

    let mut progress_log_file = match &progress_log_path {
        Some(path) => Some(
            fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(path)
                .map_err(|error| format!("Failed to open per-day progress log: {error}"))?,
        ),
        None => None,
    };

    // Step 19.00.53: no unconsumed secondary indexes to drop/rebuild around
    // this loop any more -- schema::create_schema (called above) already
    // ensures none exist. See schema.rs's drop_unconsumed_secondary_indexes
    // doc comment.
    for date in &requested_days {
        if let Some(file) = progress_log_file.as_mut() {
            let _ = writeln!(file, "{} START {date}", Utc::now().to_rfc3339());
        }

        let outcome = persistence::import_day(&connection, client, *date);

        if let Some(file) = progress_log_file.as_mut() {
            let _ = writeln!(file, "{} DONE {date} {}", Utc::now().to_rfc3339(), describe_outcome(&outcome));
        }

        imported_days.push(outcome);
    }

    // Close the log file explicitly before any attempt to delete it below --
    // Windows will not allow deleting a file that is still open (the same
    // class of issue as this project's documented DuckDB handle-release
    // gotcha in CLAUDE.md; the rule applies to any file handle, not just
    // DuckDB's).
    drop(progress_log_file);

    let rebuild_stats = summary_rebuild::rebuild_summary_and_org_context(&connection)
        .map_err(|error| format!("Failed to rebuild summary and org context: {error}"))?;

    let (metadata_last_completed_day_utc, metadata_last_updated_utc) = read_history_metadata_summary(&connection)
        .map_err(|error| format!("Failed to read history_metadata for the live config: {error}"))?;

    let validation_failures = validate_staging_build(connection, &staging_path, &requested_days)
        .map_err(|error| format!("Failed to run validation checks: {error}"))?;

    let succeeded = validation_failures.is_empty();

    if succeeded {
        if let Some(path) = &progress_log_path {
            let _ = fs::remove_file(path);
        }

        write_latest_validated_build_marker(&working_directory, &staging_file_name)
            .map_err(|error| format!("Failed to update the latest-validated-build marker: {error}"))?;

        let updated_live_config = GroupHistoryLiveConfig {
            active_database_file: staging_path.to_string_lossy().to_string(),
            schema_version: SCHEMA_VERSION,
            last_completed_day_utc: metadata_last_completed_day_utc,
            last_updated_utc: metadata_last_updated_utc,
            update_in_progress: false,
            update_in_progress_pid: None,
        };

        live_config::write_live_config(&live_config_directory, &updated_live_config)
            .map_err(|error| format!("Failed to write groupHistory live config: {error}"))?;

        live_config::write_swap_signal(&live_config_directory)
            .map_err(|error| format!("Failed to write database.new swap signal: {error}"))?;
    } else {
        let reverted_live_config =
            GroupHistoryLiveConfig { update_in_progress: false, update_in_progress_pid: None, ..pre_build_live_config };

        live_config::write_live_config(&live_config_directory, &reverted_live_config)
            .map_err(|error| format!("Failed to write groupHistory live config: {error}"))?;
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

/// Renamed from `allocate_new_staging_filename` at Step 19.00.55: produces
/// the `CORE.`-prefixed working-database name Section 6.9.2 introduces to
/// distinguish the unconstrained Working-folder file from its later
/// promoted, primary-keyed Live-folder copy (19.00.56), which keeps the
/// unprefixed `KillRight.History.yymmdd.##` name. `directory` is always the
/// `Working` subfolder (see `build_staging`), not the historic database
/// root.
fn allocate_new_working_filename(directory: &Path) -> io::Result<(PathBuf, String)> {
    let today = Utc::now().date_naive();
    let date_prefix = today.format("%y%m%d").to_string();
    let mut sequence: u32 = 1;

    loop {
        let filename = format!("CORE.KillRight.History.{date_prefix}.{sequence:02}.duckdb");
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

/// Computes the per-day progress log path for a staging file: the same
/// directory, same base name, with `.duckdb` replaced by
/// `PROGRESS_LOG_FILE_SUFFIX`. `staging_file_name` is always produced by
/// `allocate_new_staging_filename`, which always ends in `.duckdb`.
fn progress_log_path_for(directory: &Path, staging_file_name: &str) -> PathBuf {
    let base_name = staging_file_name
        .strip_suffix(".duckdb")
        .expect("staging file name always ends with .duckdb");

    directory.join(format!("{base_name}{PROGRESS_LOG_FILE_SUFFIX}"))
}

/// Formats a single `ImportDayOutcome` as one line for the per-day progress
/// log. Deliberately terse (day-level status only, not full timing) -- this
/// log exists to answer "which day was in progress when the process died,"
/// not to duplicate the detailed report `main.rs` already prints for a
/// normal foreground run.
fn describe_outcome(outcome: &ImportDayOutcome) -> String {
    if outcome.already_completed {
        "status=AlreadyCompleted".to_string()
    } else if outcome.succeeded {
        format!(
            "status=Completed raw={} qualifying={}",
            outcome.raw_killmail_count, outcome.qualifying_killmail_count
        )
    } else {
        format!("status=Failed error={}", outcome.error_message.as_deref().unwrap_or("(unknown)"))
    }
}

fn determine_missing_days(
    connection: &Connection,
    range_mode: ImportRangeMode,
    utc_today: NaiveDate,
) -> DuckResult<Vec<NaiveDate>> {
    let latest_importable_day = utc_today.pred_opt().expect("date underflow computing latest importable day");

    let (horizon_start, horizon_end) = match range_mode {
        ImportRangeMode::DefaultTenYearLookback => (
            latest_importable_day
                .checked_sub_months(Months::new(12 * DEFAULT_INITIAL_HISTORIC_IMPORT_HORIZON_YEARS))
                .expect("date underflow computing default historic horizon"),
            latest_importable_day,
        ),
        ImportRangeMode::HorizonDaysBackFromToday(days) => (
            latest_importable_day - Duration::days((days - 1).max(0)),
            latest_importable_day,
        ),
        ImportRangeMode::AnchoredTestAmount(amount, unit) => {
            compute_anchored_test_range(connection, amount, unit, latest_importable_day)?
        }
    };

    let horizon_start_text = horizon_start.format("%Y-%m-%d").to_string();
    let horizon_end_text = horizon_end.format("%Y-%m-%d").to_string();

    let mut statement = connection.prepare(
        "SELECT import_date_utc FROM history_import_day_status \
         WHERE status = 'Completed' AND import_date_utc >= ? AND import_date_utc <= ?;",
    )?;

    let mut completed_days: HashSet<NaiveDate> = HashSet::new();
    let mut rows = statement.query(params![horizon_start_text, horizon_end_text])?;

    while let Some(row) = rows.next()? {
        let text: String = row.get(0)?;

        if let Ok(date) = NaiveDate::parse_from_str(&text, "%Y-%m-%d") {
            completed_days.insert(date);
        }
    }

    let mut missing_days = Vec::new();
    let mut current = horizon_start;

    while current <= horizon_end {
        if !completed_days.contains(&current) {
            missing_days.push(current);
        }

        current = current.succ_opt().expect("date overflow while building horizon range");
    }

    Ok(missing_days)
}

/// Reads the staging build's own `history_metadata.last_completed_day_utc` and
/// `last_update_utc` columns, already maintained per-day by `persistence::import_day`
/// since 19.00.45, for use as the live config's `lastCompletedDayUtc`/`lastUpdatedUtc`
/// fields. Must be called while `connection` is still open, before
/// `validate_staging_build` takes ownership of it and closes it.
fn read_history_metadata_summary(connection: &Connection) -> DuckResult<(Option<String>, Option<String>)> {
    connection.query_row(
        "SELECT last_completed_day_utc, last_update_utc FROM history_metadata LIMIT 1;",
        [],
        |row| {
            let last_completed_day_utc: Option<String> = row.get(0)?;
            let last_updated_utc: Option<String> = row.get(1)?;
            Ok((last_completed_day_utc, last_updated_utc))
        },
    )
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
    // remaining lifetime. See Revision note (REV-D) in 19.00.47.
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn progress_log_path_for_replaces_duckdb_extension_with_progress_log_suffix() {
        let directory = Path::new("staging-directory");
        let path = progress_log_path_for(directory, "CORE.KillRight.History.260807.02.duckdb");

        assert_eq!(path, directory.join("CORE.KillRight.History.260807.02.progress.log"));
    }

    #[test]
    fn describe_outcome_formats_completed_day() {
        let outcome = ImportDayOutcome {
            date: NaiveDate::from_ymd_opt(2016, 8, 1).unwrap(),
            already_completed: false,
            succeeded: true,
            raw_killmail_count: 12,
            qualifying_killmail_count: 5,
            persisted_evidence_rows: 5,
            persisted_participant_rows: 11,
            candidate_pair_occurrence_rows: 20,
            timing: None,
            error_message: None,
        };

        assert_eq!(describe_outcome(&outcome), "status=Completed raw=12 qualifying=5");
    }

    #[test]
    fn describe_outcome_formats_failed_day() {
        let outcome = ImportDayOutcome {
            date: NaiveDate::from_ymd_opt(2016, 8, 1).unwrap(),
            already_completed: false,
            succeeded: false,
            raw_killmail_count: 0,
            qualifying_killmail_count: 0,
            persisted_evidence_rows: 0,
            persisted_participant_rows: 0,
            candidate_pair_occurrence_rows: 0,
            timing: None,
            error_message: Some("HTTP 503 Service Unavailable".to_string()),
        };

        assert_eq!(describe_outcome(&outcome), "status=Failed error=HTTP 503 Service Unavailable");
    }

    #[test]
    fn describe_outcome_formats_already_completed_day() {
        let outcome = ImportDayOutcome {
            date: NaiveDate::from_ymd_opt(2016, 8, 1).unwrap(),
            already_completed: true,
            succeeded: true,
            raw_killmail_count: 0,
            qualifying_killmail_count: 0,
            persisted_evidence_rows: 0,
            persisted_participant_rows: 0,
            candidate_pair_occurrence_rows: 0,
            timing: None,
            error_message: None,
        };

        assert_eq!(describe_outcome(&outcome), "status=AlreadyCompleted");
    }

    fn insert_completed_day(connection: &Connection, date_text: &str) {
        connection
            .execute(
                "INSERT INTO history_import_day_status \
                 (import_date_utc, status, raw_killmail_count, qualifying_killmail_count, \
                  participant_index_row_count, pair_occurrence_count) \
                 VALUES (?, 'Completed', 0, 0, 0, 0);",
                params![date_text],
            )
            .unwrap();
    }

    #[test]
    fn find_test_range_frontier_no_completed_days_returns_anchor_predecessor() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let anchor = test_mode_anchor_start_date();
        let frontier = find_test_range_frontier(&connection, anchor).unwrap();

        assert_eq!(frontier, NaiveDate::from_ymd_opt(2016, 7, 31).unwrap());
    }

    #[test]
    fn find_test_range_frontier_returns_max_completed_day_at_or_after_anchor() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        insert_completed_day(&connection, "2016-08-03");
        insert_completed_day(&connection, "2016-08-07");
        insert_completed_day(&connection, "2016-08-05");

        let anchor = test_mode_anchor_start_date();
        let frontier = find_test_range_frontier(&connection, anchor).unwrap();

        assert_eq!(frontier, NaiveDate::from_ymd_opt(2016, 8, 7).unwrap());
    }

    #[test]
    fn find_test_range_frontier_ignores_completed_days_before_anchor() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        insert_completed_day(&connection, "2016-07-15");

        let anchor = test_mode_anchor_start_date();
        let frontier = find_test_range_frontier(&connection, anchor).unwrap();

        assert_eq!(frontier, NaiveDate::from_ymd_opt(2016, 7, 31).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_days_first_run_returns_inclusive_range_from_anchor() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let latest_importable_day = NaiveDate::from_ymd_opt(2026, 8, 4).unwrap();
        let (start, end) =
            compute_anchored_test_range(&connection, 7, TestAmountUnit::Days, latest_importable_day).unwrap();

        assert_eq!(start, NaiveDate::from_ymd_opt(2016, 8, 1).unwrap());
        assert_eq!(end, NaiveDate::from_ymd_opt(2016, 8, 7).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_months_first_run_returns_calendar_month_span() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let latest_importable_day = NaiveDate::from_ymd_opt(2026, 8, 4).unwrap();
        let (start, end) =
            compute_anchored_test_range(&connection, 1, TestAmountUnit::Months, latest_importable_day).unwrap();

        assert_eq!(start, NaiveDate::from_ymd_opt(2016, 8, 1).unwrap());
        assert_eq!(end, NaiveDate::from_ymd_opt(2016, 8, 31).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_years_first_run_returns_calendar_year_span() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let latest_importable_day = NaiveDate::from_ymd_opt(2026, 8, 4).unwrap();
        let (start, end) =
            compute_anchored_test_range(&connection, 1, TestAmountUnit::Years, latest_importable_day).unwrap();

        assert_eq!(start, NaiveDate::from_ymd_opt(2016, 8, 1).unwrap());
        assert_eq!(end, NaiveDate::from_ymd_opt(2017, 7, 31).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_is_additive_across_successive_runs() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        // Simulates a completed "1 Week" run (01-07 Aug 2016) before this call.
        for day in 1..=7 {
            insert_completed_day(&connection, &format!("2016-08-{day:02}"));
        }

        let latest_importable_day = NaiveDate::from_ymd_opt(2026, 8, 4).unwrap();
        let (start, end) =
            compute_anchored_test_range(&connection, 1, TestAmountUnit::Months, latest_importable_day).unwrap();

        // Anchor stays fixed; end extends from the Week run's frontier (07 Aug), not from
        // the anchor again -- the resulting range covers the Week plus the new Month.
        assert_eq!(start, NaiveDate::from_ymd_opt(2016, 8, 1).unwrap());
        assert_eq!(end, NaiveDate::from_ymd_opt(2016, 9, 7).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_months_clamps_frontier_day_to_end_of_shorter_month() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        insert_completed_day(&connection, "2016-08-31");

        let latest_importable_day = NaiveDate::from_ymd_opt(2026, 8, 4).unwrap();
        let (_, end) =
            compute_anchored_test_range(&connection, 1, TestAmountUnit::Months, latest_importable_day).unwrap();

        // 31 Aug + 1 month: September only has 30 days, so chrono's checked_add_months
        // clamps to the last valid day rather than erroring.
        assert_eq!(end, NaiveDate::from_ymd_opt(2016, 9, 30).unwrap());
    }

    #[test]
    fn compute_anchored_test_range_clamps_to_latest_importable_day() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let latest_importable_day = NaiveDate::from_ymd_opt(2016, 8, 5).unwrap();
        let (start, end) =
            compute_anchored_test_range(&connection, 30, TestAmountUnit::Days, latest_importable_day).unwrap();

        assert_eq!(start, NaiveDate::from_ymd_opt(2016, 8, 1).unwrap());
        assert_eq!(end, NaiveDate::from_ymd_opt(2016, 8, 5).unwrap());
    }

    #[test]
    fn determine_missing_days_default_lookback_matches_prior_behaviour() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let utc_today = NaiveDate::from_ymd_opt(2026, 8, 5).unwrap();
        let missing = determine_missing_days(&connection, ImportRangeMode::DefaultTenYearLookback, utc_today).unwrap();

        assert_eq!(missing.first().unwrap(), &NaiveDate::from_ymd_opt(2016, 8, 4).unwrap());
        assert_eq!(missing.last().unwrap(), &NaiveDate::from_ymd_opt(2026, 8, 4).unwrap());
    }

    #[test]
    fn determine_missing_days_horizon_days_back_from_today_matches_prior_behaviour() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let utc_today = NaiveDate::from_ymd_opt(2026, 8, 5).unwrap();
        let missing =
            determine_missing_days(&connection, ImportRangeMode::HorizonDaysBackFromToday(1), utc_today).unwrap();

        assert_eq!(missing, vec![NaiveDate::from_ymd_opt(2026, 8, 4).unwrap()]);
    }

    #[test]
    fn determine_missing_days_anchored_test_amount_is_additive_and_skips_completed_week() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        // Simulates a completed "1 Week" run (01-07 Aug 2016) before this call.
        for day in 1..=7 {
            insert_completed_day(&connection, &format!("2016-08-{day:02}"));
        }

        let utc_today = NaiveDate::from_ymd_opt(2026, 8, 5).unwrap();
        let missing = determine_missing_days(
            &connection,
            ImportRangeMode::AnchoredTestAmount(1, TestAmountUnit::Months),
            utc_today,
        )
        .unwrap();

        // The already-completed week is skipped; only the newly-added days (08 Aug
        // through 07 Sep, extending the Week run's frontier by a further month) are
        // requested -- 31 days, none of them from the first week.
        assert_eq!(missing.len(), 31);
        assert_eq!(missing[0], NaiveDate::from_ymd_opt(2016, 8, 8).unwrap());
        assert_eq!(*missing.last().unwrap(), NaiveDate::from_ymd_opt(2016, 9, 7).unwrap());

        for day in 1..=7 {
            assert!(!missing.contains(&NaiveDate::from_ymd_opt(2016, 8, day).unwrap()));
        }
    }
}
