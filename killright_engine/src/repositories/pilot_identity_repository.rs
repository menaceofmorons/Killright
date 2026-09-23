use std::path::PathBuf;

use chrono::{DateTime, Utc};

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

#[derive(Debug, Clone, PartialEq)]
pub struct PilotIdentitySnapshot {
    pub input_name: String,
    pub character_id: Option<i64>,
    pub character_name: Option<String>,
    pub verify_status: String,
    pub security_status: Option<f64>,
    pub corporation_id: Option<i64>,
    pub corporation_name: Option<String>,
    pub corporation_ticker: Option<String>,
    pub alliance_id: Option<i64>,
    pub alliance_name: Option<String>,
    pub alliance_ticker: Option<String>,
    pub cached_at_utc: String,
}

pub struct PilotIdentityRepository {
    database_path: PathBuf,
}

impl PilotIdentityRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_for_character(
        &self,
        character_id: i64,
    ) -> RepositoryResult<Option<PilotIdentitySnapshot>> {
        let connection = open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT input_name, character_id, character_name, verify_status, security_status, corporation_id, corporation_name, corporation_ticker, alliance_id, alliance_name, alliance_ticker, cached_at_utc \
             FROM main.pilot_identity_cache \
             WHERE character_id = {} \
             LIMIT 1;",
            character_id);

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;

        if let Some(row) = rows.next()? {
            let cached_at_utc: DateTime<Utc> = row.get(11)?;

            return Ok(Some(PilotIdentitySnapshot {
                input_name: row.get(0)?,
                character_id: row.get(1)?,
                character_name: row.get(2)?,
                verify_status: row.get(3)?,
                security_status: row.get(4)?,
                corporation_id: row.get(5)?,
                corporation_name: row.get(6)?,
                corporation_ticker: row.get(7)?,
                alliance_id: row.get(8)?,
                alliance_name: row.get(9)?,
                alliance_ticker: row.get(10)?,
                cached_at_utc: cached_at_utc.to_rfc3339(),
            }));
        }

        Ok(None)
    }

    pub fn get_for_characters(
        &self,
        character_ids: &[i64],
    ) -> RepositoryResult<Vec<PilotIdentitySnapshot>> {
        if character_ids.is_empty() {
            return Ok(Vec::new());
        }

        let connection = open_connection(&self.database_path)?;

        let ids = character_ids
            .iter()
            .map(|character_id| character_id.to_string())
            .collect::<Vec<_>>()
            .join(",");

        let sql = format!(
            "SELECT input_name, character_id, character_name, verify_status, security_status, corporation_id, corporation_name, corporation_ticker, alliance_id, alliance_name, alliance_ticker, cached_at_utc \
             FROM main.pilot_identity_cache \
             WHERE character_id IN ({ids});");

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;
        let mut results = Vec::new();

        while let Some(row) = rows.next()? {
            let cached_at_utc: DateTime<Utc> = row.get(11)?;

            results.push(PilotIdentitySnapshot {
                input_name: row.get(0)?,
                character_id: row.get(1)?,
                character_name: row.get(2)?,
                verify_status: row.get(3)?,
                security_status: row.get(4)?,
                corporation_id: row.get(5)?,
                corporation_name: row.get(6)?,
                corporation_ticker: row.get(7)?,
                alliance_id: row.get(8)?,
                alliance_name: row.get(9)?,
                alliance_ticker: row.get(10)?,
                cached_at_utc: cached_at_utc.to_rfc3339(),
            });
        }

        Ok(results)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use duckdb::Connection;

    fn create_schema(connection: &Connection) {
        connection
            .execute_batch(
                "CREATE TABLE pilot_identity_cache (
                    input_name TEXT PRIMARY KEY,
                    character_id BIGINT,
                    character_name TEXT,
                    verify_status TEXT NOT NULL,
                    security_status DOUBLE,
                    corporation_id BIGINT,
                    corporation_name TEXT,
                    corporation_ticker TEXT,
                    alliance_id BIGINT,
                    alliance_name TEXT,
                    alliance_ticker TEXT,
                    cached_at_utc TIMESTAMP NOT NULL
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
    fn get_for_characters_returns_rows_for_requested_characters_only() {
        let path = std::env::temp_dir().join(format!("pilot-identity-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection
            .execute_batch(
                "INSERT INTO pilot_identity_cache VALUES
                    ('Lukas Naarii', 95465499, 'Lukas Naarii', 'Verified', 1.2, 98000001, 'Corp One', 'ONE', NULL, NULL, NULL, '2026-09-20T00:00:00+00:00'),
                    ('T''ral Vsengne', 91321792, 'T''ral Vsengne', 'Verified', 0.4, 98000002, 'Corp Two', 'TWO', 99000001, 'Alliance Two', 'ATWO', '2026-09-20T00:00:00+00:00'),
                    ('syMptom NZ', 90000003, 'syMptom NZ', 'Verified', -1.0, 98000003, 'Corp Three', 'THR', NULL, NULL, NULL, '2026-09-20T00:00:00+00:00');",
            )
            .unwrap();
        drop(connection);

        let repository = PilotIdentityRepository::new(path.clone());
        let rows = repository
            .get_for_characters(&[95465499, 91321792])
            .unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(rows.len(), 2);
        assert!(rows.iter().any(|row| row.character_id == Some(95465499)));
        assert!(rows.iter().any(|row| row.character_id == Some(91321792)));
        assert!(!rows.iter().any(|row| row.character_id == Some(90000003)));
    }

    #[test]
    fn get_for_characters_empty_scan_set_returns_empty() {
        let path = std::env::temp_dir().join(format!("pilot-identity-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);
        drop(connection);

        let repository = PilotIdentityRepository::new(path.clone());
        let rows = repository.get_for_characters(&[]).unwrap();

        std::fs::remove_file(&path).unwrap();

        assert!(rows.is_empty());
    }
}
