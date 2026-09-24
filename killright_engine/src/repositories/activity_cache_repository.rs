use std::path::PathBuf;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

pub struct ActivityCacheRepository {
    database_path: PathBuf,
}

impl ActivityCacheRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
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
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
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
}
