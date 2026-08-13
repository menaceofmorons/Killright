use std::env;
use std::path::PathBuf;
use std::process;

mod affiliation_timeline;
mod database_path;
mod episode_builder;
mod esi_active_status;
mod folder_layout;
mod live_config;
mod local_app_data;
mod lock;
mod persistence;
mod promotion;
mod r2_client;
mod rate_limiter;
mod schema;
mod staging;
mod status;
mod summary_rebuild;

use chrono::NaiveDate;
use database_path::{get_default_database_path, get_default_lock_path, get_history_updater_directory};
use duckdb::Connection;
use episode_builder::{build_same_c_a_episodes, fetch_pilot_affiliation_segments};
use esi_active_status::{EntityType, EsiActiveStatusClient};
use lock::SingleInstanceLock;
use persistence::{import_day, ImportDayOutcome};
use r2_client::{EvidenceDayResult, ParallelDownloadOptions, ZkillHistoryClient};
use staging::{build_staging, ImportRangeMode, StagingBuildOutcome, TestAmountUnit};
use summary_rebuild::{rebuild_summary_and_org_context, RebuildStats};

fn main() {
    let arguments: Vec<String> = env::args().collect();

    let command = match arguments.get(1) {
        Some(value) => value.as_str(),
        None => {
            print_usage();
            process::exit(1);
        }
    };

    match command {
        "create-schema" => run_create_schema(),
        "print-status" => status::print_status(&get_default_database_path()),
        "extract-day-evidence" => run_extract_day_evidence(&arguments),
        "extract-range-evidence" => run_extract_range_evidence(&arguments),
        "check-entity-status" => run_check_entity_status(&arguments),
        "import-day" => run_import_day(&arguments),
        "print-affiliation-timeline-summary" => run_print_affiliation_timeline_summary(),
        "print-same-c-a-episodes" => run_print_same_c_a_episodes(&arguments),
        "rebuild-summary" => run_rebuild_summary(),
        "build-staging" => run_build_staging(&arguments),
        "__verify-open" => run_verify_open(&arguments),
        _ => {
            eprintln!("Unrecognised command: {command}");
            print_usage();
            process::exit(1);
        }
    }
}

fn run_create_schema() {
    let lock_path = get_default_lock_path();

    let lock = match SingleInstanceLock::acquire(&lock_path) {
        Ok(Some(lock)) => lock,
        Ok(None) => return,
        Err(error) => {
            eprintln!("Failed to acquire history update lock: {error}");
            process::exit(1);
        }
    };

    let database_path = get_default_database_path();
    println!("Creating historic schema at {}", database_path.display());

    let connection = match Connection::open(&database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            drop(lock);
            process::exit(1);
        }
    };

    if let Err(error) = schema::create_schema(&connection) {
        eprintln!("Failed to create schema: {error}");
        drop(lock);
        process::exit(1);
    }

    let created_utc = chrono::Utc::now().to_rfc3339();

    if let Err(error) = schema::insert_initial_metadata_row(&connection, &created_utc) {
        eprintln!("Failed to insert initial metadata row: {error}");
        drop(lock);
        process::exit(1);
    }

    println!("Historic schema created successfully.");
    drop(lock);
}

fn run_extract_day_evidence(arguments: &[String]) {
    let date = match arguments.get(2).and_then(|text| NaiveDate::parse_from_str(text, "%Y-%m-%d").ok()) {
        Some(value) => value,
        None => {
            eprintln!("Usage: killright_history_updater extract-day-evidence <yyyy-mm-dd>");
            process::exit(1);
        }
    };

    let client = match ZkillHistoryClient::new(ParallelDownloadOptions::default()) {
        Ok(client) => client,
        Err(error) => {
            eprintln!("Failed to create HTTP client: {error}");
            process::exit(1);
        }
    };

    let result = client.extract_day_evidence(date, None);
    print_evidence_day_report(&result);

    if !result.day_result.succeeded {
        process::exit(1);
    }
}

fn run_extract_range_evidence(arguments: &[String]) {
    let start_date = arguments.get(2).and_then(|text| NaiveDate::parse_from_str(text, "%Y-%m-%d").ok());
    let end_date = arguments.get(3).and_then(|text| NaiveDate::parse_from_str(text, "%Y-%m-%d").ok());

    let (start_date, end_date) = match (start_date, end_date) {
        (Some(start), Some(end)) if start <= end => (start, end),
        _ => {
            eprintln!("Usage: killright_history_updater extract-range-evidence <start yyyy-mm-dd> <end yyyy-mm-dd>");
            process::exit(1);
        }
    };

    let mut dates = Vec::new();
    let mut current = start_date;

    while current <= end_date {
        dates.push(current);
        current = current.succ_opt().expect("date overflow while building date range");
    }

    let client = match ZkillHistoryClient::new(ParallelDownloadOptions::default()) {
        Ok(client) => client,
        Err(error) => {
            eprintln!("Failed to create HTTP client: {error}");
            process::exit(1);
        }
    };

    let results = client.extract_days_evidence(&dates);

    let mut total_raw = 0i64;
    let mut total_qualifying = 0i64;
    let mut total_participants = 0i64;
    let mut failed_days = 0i64;

    for result in &results {
        print_evidence_day_report(result);
        total_raw += result.day_result.raw_killmail_count as i64;
        total_qualifying += result.day_result.qualifying_killmail_count as i64;
        total_participants += result.participant_rows.len() as i64;

        if !result.day_result.succeeded {
            failed_days += 1;
        }
    }

    println!("====================================================");
    println!("Range summary: {start_date} through {end_date}");
    println!("Days processed: {}", results.len());
    println!("Failed days: {failed_days}");
    println!("Total raw killmails: {total_raw}");
    println!("Total qualifying killmails: {total_qualifying}");
    println!("Total participant rows: {total_participants}");
    println!("====================================================");

    if failed_days > 0 {
        process::exit(1);
    }
}

/// Diagnostic command: checks a single corporation or alliance's ESI
/// active status directly, with no database involved -- no rows are
/// written or read from any database, matching extract-day-evidence's own
/// no-database-touch diagnostic pattern. Exists so a real ESI check can be
/// confirmed against a real, live entity ID without running the full
/// check-and-record path (esi_active_status::ensure_entity_active_status_cached).
fn run_check_entity_status(arguments: &[String]) {
    let entity_type = match arguments.get(2).map(String::as_str) {
        Some("corporation") => EntityType::Corporation,
        Some("alliance") => EntityType::Alliance,
        _ => {
            eprintln!("Usage: killright_history_updater check-entity-status <corporation|alliance> <id>");
            process::exit(1);
        }
    };

    let entity_id: i64 = match arguments.get(3).and_then(|text| text.parse().ok()) {
        Some(value) => value,
        None => {
            eprintln!("Usage: killright_history_updater check-entity-status <corporation|alliance> <id>");
            process::exit(1);
        }
    };

    let client = match EsiActiveStatusClient::new() {
        Ok(client) => client,
        Err(error) => {
            eprintln!("Failed to create HTTP client: {error}");
            process::exit(1);
        }
    };

    let result = match entity_type {
        EntityType::Corporation => client.check_corporation_active(entity_id),
        EntityType::Alliance => client.check_alliance_active(entity_id),
    };

    println!("====================================================");
    println!("KillRight Historic Updater - Entity Active-Status Check");
    println!("====================================================");
    println!("Entity type: {}", entity_type.as_label());
    println!("Entity ID: {entity_id}");

    match result {
        Ok(true) => {
            println!("Status: Active");
            println!("Notes:");
            println!("- No rows are written to or read from any database in this step.");
            println!("====================================================");
        }
        Ok(false) => {
            println!("Status: Closed");
            println!("Notes:");
            println!("- No rows are written to or read from any database in this step.");
            println!("====================================================");
        }
        Err(error) => {
            println!("Status: Error");
            println!("Error: {error}");
            println!("====================================================");
            process::exit(1);
        }
    }
}

fn run_import_day(arguments: &[String]) {
    let date = match arguments.get(2).and_then(|text| NaiveDate::parse_from_str(text, "%Y-%m-%d").ok()) {
        Some(value) => value,
        None => {
            eprintln!("Usage: killright_history_updater import-day <yyyy-mm-dd>");
            process::exit(1);
        }
    };

    let lock_path = get_default_lock_path();

    let lock = match SingleInstanceLock::acquire(&lock_path) {
        Ok(Some(lock)) => lock,
        Ok(None) => return,
        Err(error) => {
            eprintln!("Failed to acquire history update lock: {error}");
            process::exit(1);
        }
    };

    let database_path = get_default_database_path();

    let connection = match Connection::open(&database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            drop(lock);
            process::exit(1);
        }
    };

    if let Err(error) = schema::create_schema(&connection) {
        eprintln!("Failed to ensure schema: {error}");
        drop(lock);
        process::exit(1);
    }

    let created_utc = chrono::Utc::now().to_rfc3339();

    if let Err(error) = schema::insert_initial_metadata_row(&connection, &created_utc) {
        eprintln!("Failed to ensure initial metadata row: {error}");
        drop(lock);
        process::exit(1);
    }

    let client = match ZkillHistoryClient::new(ParallelDownloadOptions::default()) {
        Ok(client) => client,
        Err(error) => {
            eprintln!("Failed to create HTTP client: {error}");
            drop(lock);
            process::exit(1);
        }
    };

    let outcome = import_day(&connection, &client, date);
    print_import_day_report(&outcome);
    drop(lock);

    if !outcome.succeeded {
        process::exit(1);
    }
}

/// Diagnostic command: prints a summary of historic_pilot_affiliation_timeline
/// (Design Specification Section 4.7, Implementation Plan Step 19.01.03) --
/// total row count plus the five most-recently-updated rows. No rows are
/// written by this command, matching print-status's own read-only pattern.
/// Exists so real day-import behaviour (import-day, against a real date)
/// can be inspected without a specific pilot ID known in advance -- unlike
/// check-entity-status's known real ESI entity ID, a day's participants are
/// not knowable ahead of a real import run.
fn run_print_affiliation_timeline_summary() {
    let database_path = get_default_database_path();

    if !database_path.exists() {
        println!("Database does not exist at {}", database_path.display());
        return;
    }

    let connection = match Connection::open(&database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            process::exit(1);
        }
    };

    let total_row_count: i64 = match connection.query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0)) {
        Ok(value) => value,
        Err(error) => {
            eprintln!("Failed to query historic_pilot_affiliation_timeline: {error}");
            process::exit(1);
        }
    };

    println!("====================================================");
    println!("KillRight Historic Updater - Pilot Affiliation Timeline Summary");
    println!("====================================================");
    println!("Total rows: {total_row_count}");
    println!("Most recently updated rows (up to 5):");

    let mut statement = match connection.prepare(
        "SELECT pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc \
         FROM historic_pilot_affiliation_timeline \
         ORDER BY last_updated_utc DESC \
         LIMIT 5;",
    ) {
        Ok(statement) => statement,
        Err(error) => {
            eprintln!("Failed to prepare historic_pilot_affiliation_timeline query: {error}");
            process::exit(1);
        }
    };

    let rows = statement.query_map([], |row| {
        Ok((
            row.get::<_, i64>(0)?,
            row.get::<_, Option<i64>>(1)?,
            row.get::<_, Option<i64>>(2)?,
            row.get::<_, String>(3)?,
            row.get::<_, String>(4)?,
            row.get::<_, String>(5)?,
        ))
    });

    match rows {
        Ok(rows) => {
            for row in rows {
                match row {
                    Ok((pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc)) => {
                        println!(
                            "- pilot_id={pilot_id} corporation_id={} alliance_id={} first_seen_utc={first_seen_utc} last_seen_utc={last_seen_utc} last_updated_utc={last_updated_utc}",
                            corporation_id.map(|value| value.to_string()).unwrap_or_else(|| "(none)".to_string()),
                            alliance_id.map(|value| value.to_string()).unwrap_or_else(|| "(none)".to_string()),
                        );
                    }
                    Err(error) => {
                        eprintln!("Failed to read a historic_pilot_affiliation_timeline row: {error}");
                        process::exit(1);
                    }
                }
            }
        }
        Err(error) => {
            eprintln!("Failed to query historic_pilot_affiliation_timeline: {error}");
            process::exit(1);
        }
    }

    println!("Notes:");
    println!("- Diagnostic command: no rows are written by this command.");
    println!("====================================================");
}

/// Diagnostic command: prints the Same C/A episode list between two real
/// pilots (Design Specification Section 6.11.4, Implementation Plan Step
/// 19.01.04) by fetching both pilots' `historic_pilot_affiliation_timeline`
/// history and running `build_same_c_a_episodes` against them. No rows are
/// written by this command, matching `print-affiliation-timeline-summary`'s
/// own read-only pattern -- exists purely so this step's Developer
/// verification test (Section 7.2) can be run against real data, matching
/// Section 6.11.3's stated Phase 1 scope (no query interface exists for
/// this component yet).
fn run_print_same_c_a_episodes(arguments: &[String]) {
    let pilot_a_id: i64 = match arguments.get(2).and_then(|text| text.parse().ok()) {
        Some(value) => value,
        None => {
            eprintln!("Usage: killright_history_updater print-same-c-a-episodes <pilot_a_id> <pilot_b_id>");
            process::exit(1);
        }
    };

    let pilot_b_id: i64 = match arguments.get(3).and_then(|text| text.parse().ok()) {
        Some(value) => value,
        None => {
            eprintln!("Usage: killright_history_updater print-same-c-a-episodes <pilot_a_id> <pilot_b_id>");
            process::exit(1);
        }
    };

    let database_path = get_default_database_path();

    if !database_path.exists() {
        println!("Database does not exist at {}", database_path.display());
        return;
    }

    let connection = match Connection::open(&database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            process::exit(1);
        }
    };

    let timeline_a = match fetch_pilot_affiliation_segments(&connection, pilot_a_id) {
        Ok(segments) => segments,
        Err(error) => {
            eprintln!("Failed to fetch pilot_id {pilot_a_id}'s affiliation timeline: {error}");
            process::exit(1);
        }
    };

    let timeline_b = match fetch_pilot_affiliation_segments(&connection, pilot_b_id) {
        Ok(segments) => segments,
        Err(error) => {
            eprintln!("Failed to fetch pilot_id {pilot_b_id}'s affiliation timeline: {error}");
            process::exit(1);
        }
    };

    println!("====================================================");
    println!("KillRight Historic Updater - Same C/A Episodes");
    println!("====================================================");
    println!("Pilot A: {pilot_a_id} ({} timeline row(s))", timeline_a.len());
    println!("Pilot B: {pilot_b_id} ({} timeline row(s))", timeline_b.len());

    let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

    if episodes.is_empty() {
        println!("Episodes: none");
    } else {
        println!("Episodes ({}):", episodes.len());
        for episode in &episodes {
            println!("- {} to {}", episode.start_utc.to_rfc3339(), episode.end_utc.to_rfc3339());
        }
    }

    println!("Notes:");
    println!("- Diagnostic command: no rows are written by this command.");
    println!("====================================================");
}

fn run_rebuild_summary() {
    let lock_path = get_default_lock_path();

    let lock = match SingleInstanceLock::acquire(&lock_path) {
        Ok(Some(lock)) => lock,
        Ok(None) => return,
        Err(error) => {
            eprintln!("Failed to acquire history update lock: {error}");
            process::exit(1);
        }
    };

    let database_path = get_default_database_path();

    let connection = match Connection::open(&database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            drop(lock);
            process::exit(1);
        }
    };

    if let Err(error) = schema::create_schema(&connection) {
        eprintln!("Failed to ensure schema: {error}");
        drop(lock);
        process::exit(1);
    }

    let created_utc = chrono::Utc::now().to_rfc3339();

    if let Err(error) = schema::insert_initial_metadata_row(&connection, &created_utc) {
        eprintln!("Failed to ensure initial metadata row: {error}");
        drop(lock);
        process::exit(1);
    }

    match rebuild_summary_and_org_context(&connection) {
        Ok(stats) => {
            print_rebuild_summary_report(&stats);
            drop(lock);
        }
        Err(error) => {
            eprintln!("Failed to rebuild summary and org context: {error}");
            drop(lock);
            process::exit(1);
        }
    }
}

fn run_build_staging(arguments: &[String]) {
    let range_mode = match parse_import_range_mode(arguments) {
        Ok(value) => value,
        Err(message) => {
            eprintln!("{message}");
            process::exit(1);
        }
    };

    let repair_from = match parse_repair_from(arguments) {
        Ok(value) => value,
        Err(message) => {
            eprintln!("{message}");
            process::exit(1);
        }
    };

    let lock_path = get_default_lock_path();

    let lock = match SingleInstanceLock::acquire(&lock_path) {
        Ok(Some(lock)) => lock,
        Ok(None) => return,
        Err(error) => {
            eprintln!("Failed to acquire history update lock: {error}");
            process::exit(1);
        }
    };

    let directory = get_history_updater_directory();

    let client = match ZkillHistoryClient::new(ParallelDownloadOptions::default()) {
        Ok(client) => client,
        Err(error) => {
            eprintln!("Failed to create HTTP client: {error}");
            drop(lock);
            process::exit(1);
        }
    };

    match build_staging(&directory, &client, range_mode, repair_from.as_deref()) {
        Ok(outcome) => {
            let succeeded = outcome.succeeded;
            print_staging_build_report(&outcome);
            drop(lock);

            if !succeeded {
                process::exit(1);
            }
        }
        Err(message) => {
            eprintln!("Staging build failed: {message}");
            drop(lock);
            process::exit(1);
        }
    }
}

fn parse_import_range_mode(arguments: &[String]) -> Result<ImportRangeMode, String> {
    const USAGE: &str = "Usage: killright_history_updater build-staging [--horizon-days <n>] [--test-amount <n> --test-amount-unit days|months|years] [--range-start <yyyy-mm-dd> --range-end <yyyy-mm-dd>] [--repair <path>]";

    let mut horizon_days: Option<i64> = None;
    let mut test_amount: Option<i64> = None;
    let mut test_amount_unit: Option<TestAmountUnit> = None;
    let mut range_start: Option<NaiveDate> = None;
    let mut range_end: Option<NaiveDate> = None;

    let mut index = 2;

    while index < arguments.len() {
        match arguments[index].as_str() {
            "--horizon-days" => {
                let value = arguments.get(index + 1).ok_or_else(|| USAGE.to_string())?;
                let parsed: i64 = value.parse().map_err(|_| USAGE.to_string())?;

                if parsed < 1 {
                    return Err("--horizon-days must be at least 1.".to_string());
                }

                horizon_days = Some(parsed);
                index += 2;
            }
            "--test-amount" => {
                let value = arguments.get(index + 1).ok_or_else(|| USAGE.to_string())?;
                let parsed: i64 = value.parse().map_err(|_| USAGE.to_string())?;

                if parsed < 1 {
                    return Err("--test-amount must be at least 1.".to_string());
                }

                test_amount = Some(parsed);
                index += 2;
            }
            "--test-amount-unit" => {
                let value = arguments.get(index + 1).ok_or_else(|| USAGE.to_string())?;

                test_amount_unit = Some(match value.as_str() {
                    "days" => TestAmountUnit::Days,
                    "months" => TestAmountUnit::Months,
                    "years" => TestAmountUnit::Years,
                    _ => return Err("--test-amount-unit must be one of: days, months, years.".to_string()),
                });

                index += 2;
            }
            "--range-start" => {
                let value = arguments.get(index + 1).ok_or_else(|| USAGE.to_string())?;
                let parsed = NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| USAGE.to_string())?;

                range_start = Some(parsed);
                index += 2;
            }
            "--range-end" => {
                let value = arguments.get(index + 1).ok_or_else(|| USAGE.to_string())?;
                let parsed = NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| USAGE.to_string())?;

                range_end = Some(parsed);
                index += 2;
            }
            _ => {
                index += 1;
            }
        }
    }

    match (horizon_days, test_amount, test_amount_unit, range_start, range_end) {
        (Some(_), Some(_), _, _, _) => Err("--horizon-days and --test-amount are mutually exclusive.".to_string()),
        (Some(_), _, _, Some(_), _) | (Some(_), _, _, _, Some(_)) => {
            Err("--horizon-days and --range-start/--range-end are mutually exclusive.".to_string())
        }
        (_, Some(_), _, Some(_), _) | (_, Some(_), _, _, Some(_)) => {
            Err("--test-amount and --range-start/--range-end are mutually exclusive.".to_string())
        }
        (Some(days), None, _, None, None) => Ok(ImportRangeMode::HorizonDaysBackFromToday(days)),
        (None, Some(amount), Some(unit), None, None) => Ok(ImportRangeMode::AnchoredTestAmount(amount, unit)),
        (None, Some(_), None, None, None) => Err("--test-amount requires --test-amount-unit days|months|years.".to_string()),
        (None, None, Some(_), None, None) => Err("--test-amount-unit requires --test-amount <n>.".to_string()),
        (None, None, _, Some(start), Some(end)) if start <= end => Ok(ImportRangeMode::ExplicitRange(start, end)),
        (None, None, _, Some(_), Some(_)) => Err("--range-start must not be after --range-end.".to_string()),
        (None, None, _, Some(_), None) => Err("--range-start requires --range-end.".to_string()),
        (None, None, _, None, Some(_)) => Err("--range-end requires --range-start.".to_string()),
        (None, None, None, None, None) => Ok(ImportRangeMode::DefaultTenYearLookback),
    }
}

/// Step 19.00.63: parses an optional `--repair <path>` override,
/// independent of `parse_import_range_mode` above (a build can be repaired
/// under any range mode, though `--range-start`/`--range-end` is the one
/// that makes sense for it -- so this is intentionally its own small scan
/// rather than folded into that function's own flag matching). Returns
/// `Ok(None)` when the flag is absent, leaving `build_staging`'s existing
/// `find_copy_basis` lookup as the copy-basis source, exactly as before
/// this step.
fn parse_repair_from(arguments: &[String]) -> Result<Option<PathBuf>, String> {
    let mut index = 2;

    while index < arguments.len() {
        if arguments[index] == "--repair" {
            let value = arguments
                .get(index + 1)
                .ok_or_else(|| "--repair requires a file path.".to_string())?;

            return Ok(Some(PathBuf::from(value)));
        }

        index += 1;
    }

    Ok(None)
}

/// Internal-only command used by `staging::reopen_cleanly` to prove a staging
/// file opens cleanly from a genuinely separate OS process, rather than from
/// within the process that just wrote and dropped its own connection to it.
/// Not part of the public CLI surface: intentionally omitted from `print_usage`.
fn run_verify_open(arguments: &[String]) {
    let path = match arguments.get(2) {
        Some(value) => value,
        None => {
            eprintln!("__verify-open requires a database file path argument.");
            process::exit(2);
        }
    };

    match Connection::open(path) {
        Ok(connection) => {
            drop(connection);
            process::exit(0);
        }
        Err(error) => {
            eprintln!("{error}");
            process::exit(1);
        }
    }
}

fn print_evidence_day_report(result: &EvidenceDayResult) {
    let day_result = &result.day_result;

    println!("====================================================");
    println!("KillRight Historic Updater - Day Evidence Extraction");
    println!("====================================================");
    println!("Date: {}", day_result.date);
    println!("Source: {}", day_result.url);

    if !day_result.succeeded {
        println!("Status: Failed");
        println!("Error: {}", day_result.error_message.as_deref().unwrap_or("(unknown)"));
        println!("====================================================");
        return;
    }

    println!("Status: Completed");
    println!("Raw killmails: {}", day_result.raw_killmail_count);
    println!("Raw attackers: {}", day_result.raw_attacker_count);
    println!("Pod killmails excluded: {}", day_result.pod_killmail_count);
    println!(
        "Solo or insufficient attacker killmails excluded: {}",
        day_result.insufficient_attacker_killmail_count
    );
    println!("Fleet killmails excluded: {}", day_result.fleet_killmail_count);
    println!("Qualifying killmails: {}", day_result.qualifying_killmail_count);
    println!("Evidence rows in memory: {}", result.evidence_rows.len());
    println!("Qualifying attackers: {}", day_result.qualifying_attacker_count);
    println!("Participant rows in memory: {}", result.participant_rows.len());
    println!("Candidate pair occurrence rows: {}", day_result.candidate_pair_occurrence_rows);
    println!(
        "Highest qualifying attackers on a single killmail: {}",
        day_result.max_qualifying_attackers_on_killmail
    );
    println!("Notes:");
    println!("- No rows are written to any database in this step.");
    println!("- Evidence rows in memory should equal qualifying killmails.");
    println!("- Participant rows in memory should equal qualifying attackers.");
    println!("====================================================");
}

fn print_import_day_report(outcome: &ImportDayOutcome) {
    println!("====================================================");
    println!("KillRight Historic Updater - Day Import");
    println!("====================================================");
    println!("Date: {}", outcome.date);

    if outcome.already_completed {
        println!("Status: Already Completed");
        println!("No download or persistence was performed.");
        println!("====================================================");
        return;
    }

    if !outcome.succeeded {
        println!("Status: Failed");
        println!("Error: {}", outcome.error_message.as_deref().unwrap_or("(unknown)"));
        println!("====================================================");
        return;
    }

    println!("Status: Completed");
    println!("Raw killmails: {}", outcome.raw_killmail_count);
    println!("Qualifying killmails: {}", outcome.qualifying_killmail_count);
    println!("Persisted evidence rows: {}", outcome.persisted_evidence_rows);
    println!("Persisted participant rows: {}", outcome.persisted_participant_rows);
    println!("Candidate pair occurrence rows: {}", outcome.candidate_pair_occurrence_rows);
    println!("Affiliation timeline pilots touched: {}", outcome.affiliation_timeline_updated_pilot_count);

    if let Some(timing) = &outcome.timing {
        println!(
            "Timing (ms): clear={} evidence_append={} participant_append={} affiliation_timeline={} status_update={} total={}",
            timing.clear_existing_rows_elapsed_ms,
            timing.evidence_append_elapsed_ms,
            timing.participant_append_elapsed_ms,
            timing.affiliation_timeline_elapsed_ms,
            timing.status_update_elapsed_ms,
            timing.total_elapsed_ms
        );
    }

    println!("Notes:");
    println!("- Persisted evidence rows should equal qualifying killmails.");
    println!("- Persisted participant rows should equal the qualifying attacker count.");
    println!("- No historic_relationship_summary rows are written by this step.");
    println!("====================================================");
}

fn print_rebuild_summary_report(stats: &RebuildStats) {
    println!("====================================================");
    println!("KillRight Historic Updater - Summary and Org-Context Rebuild");
    println!("====================================================");
    println!("historic_relationship_summary rows: {}", stats.summary_rows);
    println!("historic_relationship_org_context rows: {}", stats.org_context_rows);
    println!(
        "Timing (ms): pair_event_scan={} summary_insert={} org_context_insert={} total={}",
        stats.pair_event_scan_elapsed_ms, stats.summary_insert_elapsed_ms, stats.org_context_insert_elapsed_ms, stats.total_elapsed_ms
    );
    println!("Notes:");
    println!("- This is a full rebuild; existing summary and org-context rows are replaced, not incrementally updated.");
    println!("- shared_event_count_linked + shared_event_count_unlinked should equal shared_event_count for the same pair.");
    println!("====================================================");
}

fn print_staging_build_report(outcome: &StagingBuildOutcome) {
    println!("====================================================");
    println!("KillRight Historic Updater - Staging Build");
    println!("====================================================");
    println!("Staging file: {}", outcome.staging_file_name);
    println!(
        "Copied from: {}",
        outcome.copied_from.as_deref().unwrap_or("(first-ever build, no copy source)")
    );
    println!("Requested days: {}", outcome.requested_days.len());

    let already_completed_days = outcome.imported_days.iter().filter(|day| day.already_completed).count();
    let newly_completed_days = outcome
        .imported_days
        .iter()
        .filter(|day| day.succeeded && !day.already_completed)
        .count();
    let failed_days = outcome
        .imported_days
        .iter()
        .filter(|day| !day.succeeded && !day.already_completed)
        .count();

    println!("Already completed (skipped): {already_completed_days}");
    println!("Newly imported: {newly_completed_days}");
    println!("Failed: {failed_days}");
    println!("historic_relationship_summary rows: {}", outcome.rebuild_stats.summary_rows);
    println!("historic_relationship_org_context rows: {}", outcome.rebuild_stats.org_context_rows);
    println!(
        "Summary/org-context rebuild timing (ms): pair_event_scan={} summary_insert={} org_context_insert={} total={}",
        outcome.rebuild_stats.pair_event_scan_elapsed_ms,
        outcome.rebuild_stats.summary_insert_elapsed_ms,
        outcome.rebuild_stats.org_context_insert_elapsed_ms,
        outcome.rebuild_stats.total_elapsed_ms
    );
    println!(
        "Promotion/validation timing (ms): copy_to_live={} primary_key_add={} validation_checks={} total={}",
        outcome.promotion_timing.copy_to_live_elapsed_ms,
        outcome.promotion_timing.primary_key_add_elapsed_ms,
        outcome.promotion_timing.validation_checks_elapsed_ms,
        outcome.promotion_timing.total_elapsed_ms
    );

    let promoted_file_name = outcome
        .promoted_file_path
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or("?");

    if outcome.validation_failures.is_empty() {
        println!("Promoted file: {promoted_file_name} (Live)");
        println!("Validation: Passed");
        println!("Status: Completed");
    } else {
        println!("Promoted file: {promoted_file_name} (Failed)");
        println!("Validation: Failed");

        for failure in &outcome.validation_failures {
            println!("- [{}] {}", failure.check_name, failure.detail);
        }

        println!("Status: Failed");
        println!("The previous validated Live-folder database (if any) remains untouched.");
        println!("This build's Working file and its promoted Failed-folder copy have both been retained for diagnosis.");
    }

    println!("====================================================");
}

fn print_usage() {
    eprintln!(
        "Usage: killright_history_updater <create-schema|print-status|extract-day-evidence|extract-range-evidence|check-entity-status|import-day|print-affiliation-timeline-summary|print-same-c-a-episodes|rebuild-summary|build-staging>"
    );
}
