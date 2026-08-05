use std::path::Path;
use std::process;

use duckdb::Connection;

pub fn print_status(database_path: &Path) {
    if !database_path.exists() {
        println!("Database does not exist at {}", database_path.display());
        return;
    }

    let connection = match Connection::open(database_path) {
        Ok(connection) => connection,
        Err(error) => {
            eprintln!("Failed to open database: {error}");
            process::exit(1);
        }
    };

    let table_exists = connection
        .query_row(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'history_metadata';",
            [],
            |row| row.get::<_, i64>(0),
        )
        .map(|count| count > 0)
        .unwrap_or(false);

    if !table_exists {
        println!("Database exists but the schema has not been created.");
        return;
    }

    let result = connection.query_row(
        "SELECT schema_version, history_start_day_utc, last_completed_day_utc, created_utc, \
                last_update_utc, average_import_milliseconds_per_day, last_import_result \
         FROM history_metadata LIMIT 1;",
        [],
        |row| {
            Ok((
                row.get::<_, i32>(0)?,
                row.get::<_, Option<String>>(1)?,
                row.get::<_, Option<String>>(2)?,
                row.get::<_, String>(3)?,
                row.get::<_, Option<String>>(4)?,
                row.get::<_, Option<i64>>(5)?,
                row.get::<_, Option<String>>(6)?,
            ))
        },
    );

    match result {
        Ok((
            schema_version,
            history_start_day_utc,
            last_completed_day_utc,
            created_utc,
            last_update_utc,
            average_import_milliseconds_per_day,
            last_import_result,
        )) => {
            println!("Schema version: {schema_version}");
            println!(
                "History start day (UTC): {}",
                history_start_day_utc.unwrap_or_else(|| "(none)".to_string())
            );
            println!(
                "Last completed day (UTC): {}",
                last_completed_day_utc.unwrap_or_else(|| "(none)".to_string())
            );
            println!("Created (UTC): {created_utc}");
            println!(
                "Last update (UTC): {}",
                last_update_utc.unwrap_or_else(|| "(none)".to_string())
            );
            println!(
                "Average import ms/day: {}",
                average_import_milliseconds_per_day
                    .map(|value| value.to_string())
                    .unwrap_or_else(|| "(none)".to_string())
            );
            println!(
                "Last import result: {}",
                last_import_result.unwrap_or_else(|| "(none)".to_string())
            );
        }
        Err(_) => {
            println!("Schema exists but history_metadata has no row yet.");
        }
    }
}
