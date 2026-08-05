use std::env;
use std::process;

mod database_path;
mod lock;
mod schema;
mod status;

use database_path::{get_default_database_path, get_default_lock_path};
use duckdb::Connection;
use lock::SingleInstanceLock;

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

fn print_usage() {
    eprintln!("Usage: killright_history_updater <create-schema|print-status>");
}
