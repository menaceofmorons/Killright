use std::collections::HashMap;
use std::path::PathBuf;

use duckdb::Connection;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

pub struct ActivityCacheRepository {
    database_path: PathBuf,
}

impl ActivityCacheRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_coverage_starts_on(
        connection: &Connection,
        character_ids: &[i64],
    ) -> RepositoryResult<HashMap<i64, String>> {
        let mut results = HashMap::new();

        if character_ids.is_empty() {
            return Ok(results);
        }

        let ids = character_ids
            .iter()
            .map(|character_id| character_id.to_string())
            .collect::<Vec<_>>()
            .join(",");

        let sql = format!(
            "SELECT character_id, recent_coverage_start_utc \
             FROM main.zkill_activity_cache \
             WHERE character_id IN ({ids});");

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;

        while let Some(row) = rows.next()? {
            let character_id: i64 = row.get(0)?;
            let coverage_start_utc: Option<String> = row.get(1)?;

            if let Some(value) = coverage_start_utc {
                results.insert(character_id, value);
            }
        }

        Ok(results)
    }

    pub fn get_coverage_start_for_character(
        &self,
        character_id: i64,
    ) -> RepositoryResult<Option<String>> {
        let connection = open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT recent_coverage_start_utc \
             FROM main.zkill_activity_cache \
             WHERE character_id = {} \
             LIMIT 1;",
            character_id);

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;

        if let Some(row) = rows.next()? {
            let coverage_start_utc: Option<String> = row.get(0)?;
            return Ok(coverage_start_utc);
        }

        Ok(None)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use duckdb::Connection;

    fn create_schema(connection: &Connection) {
        connection
            .execute_batch(
                "CREATE TABLE zkill_activity_cache (
                    character_id BIGINT PRIMARY KEY,
                    has_public_activity_data BOOLEAN NOT NULL,
                    kills_week INTEGER,
                    solo_week INTEGER,
                    last_active_utc TEXT,
                    last_activity_type TEXT,
                    checked_at_utc TEXT NOT NULL,
                    error TEXT,
                    last_recent_call_utc TEXT,
                    recent_coverage_start_utc TEXT
                );",
            )
            .unwrap();
    }

    fn unique_suffix() -> u128 {
        static COUNTER: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();

        nanos * 1000 + u128::from(COUNTER.fetch_add(1, std::sync::atomic::Ordering::SeqCst) % 1000)
    }

    #[test]
    fn get_coverage_start_for_character_returns_stored_value() {
        let path = std::env::temp_dir().join(format!("activity-cache-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection
            .execute_batch(
                "INSERT INTO zkill_activity_cache VALUES
                    (95465499, TRUE, 1, 0, NULL, NULL, '2026-09-22T00:00:00+00:00', NULL, '2026-09-20T00:00:00+00:00', '2026-09-08T00:00:00+00:00');",
            )
            .unwrap();
        drop(connection);

        let repository = ActivityCacheRepository::new(path.clone());
        let result = repository.get_coverage_start_for_character(95465499).unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(result, Some("2026-09-08T00:00:00+00:00".to_string()));
    }

    #[test]
    fn get_coverage_start_for_character_returns_none_when_missing() {
        let path = std::env::temp_dir().join(format!("activity-cache-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);
        drop(connection);

        let repository = ActivityCacheRepository::new(path.clone());
        let result = repository.get_coverage_start_for_character(95465499).unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(result, None);
    }

    #[test]
    fn get_coverage_starts_on_returns_only_pilots_with_a_stored_value() {
        let path = std::env::temp_dir().join(format!("activity-cache-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection
            .execute_batch(
                "INSERT INTO zkill_activity_cache VALUES
                    (95465499, TRUE, 1, 0, NULL, NULL, '2026-09-22T00:00:00+00:00', NULL, '2026-09-20T00:00:00+00:00', '2026-09-08T00:00:00+00:00'),
                    (91321792, TRUE, 1, 0, NULL, NULL, '2026-09-22T00:00:00+00:00', NULL, NULL, NULL);",
            )
            .unwrap();

        let starts = ActivityCacheRepository::get_coverage_starts_on(&connection, &[95465499, 91321792, 5]).unwrap();
        drop(connection);
        std::fs::remove_file(&path).unwrap();

        assert_eq!(starts.len(), 1);
        assert_eq!(starts[&95465499], "2026-09-08T00:00:00+00:00");
    }
}
