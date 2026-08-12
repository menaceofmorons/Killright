use std::env;
use std::process;

mod database_path;
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
        "import-day" => run_import_day(&arguments),
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

    match build_staging(&directory, &client, range_mode) {
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
    const USAGE: &str = "Usage: killright_history_updater build-staging [--horizon-days <n>] [--test-amount <n> --test-amount-unit days|months|years]";

    let mut horizon_days: Option<i64> = None;
    let mut test_amount: Option<i64> = None;
    let mut test_amount_unit: Option<TestAmountUnit> = None;

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
            _ => {
                index += 1;
            }
        }
    }

    match (horizon_days, test_amount, test_amount_unit) {
        (Some(_), Some(_), _) => Err("--horizon-days and --test-amount are mutually exclusive.".to_string()),
        (Some(days), None, _) => Ok(ImportRangeMode::HorizonDaysBackFromToday(days)),
        (None, Some(amount), Some(unit)) => Ok(ImportRangeMode::AnchoredTestAmount(amount, unit)),
        (None, Some(_), None) => Err("--test-amount requires --test-amount-unit days|months|years.".to_string()),
        (None, None, Some(_)) => Err("--test-amount-unit requires --test-amount <n>.".to_string()),
        (None, None, None) => Ok(ImportRangeMode::DefaultTenYearLookback),
    }
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

    if let Some(timing) = &outcome.timing {
        println!(
            "Timing (ms): clear={} evidence_append={} participant_append={} status_update={} total={}",
            timing.clear_existing_rows_elapsed_ms,
            timing.evidence_append_elapsed_ms,
            timing.participant_append_elapsed_ms,
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
        "Usage: killright_history_updater <create-schema|print-status|extract-day-evidence|extract-range-evidence|import-day|rebuild-summary|build-staging>"
    );
}
