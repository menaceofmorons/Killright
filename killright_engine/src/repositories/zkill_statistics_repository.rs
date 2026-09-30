use std::collections::HashMap;
use std::path::PathBuf;

use duckdb::Connection;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

#[derive(Debug, Clone, PartialEq)]
pub struct ZKillStatisticsSnapshot {
    pub character_id: i64,
    pub ships_destroyed: i32,
    pub solo_kills: i32,
    pub solo_ratio: f64,
    pub avg_gang_size: f64,
    pub ships_lost: i32,
    pub solo_losses: i32,
    pub general_style: String,
    pub checked_at_utc: String,
    pub no_history_marker: bool,
}

pub struct ZKillStatisticsRepository {
    database_path: PathBuf,
}

impl ZKillStatisticsRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_for_characters_on(
        connection: &Connection,
        character_ids: &[i64],
    ) -> RepositoryResult<HashMap<i64, ZKillStatisticsSnapshot>> {
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
            "SELECT character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc, no_history_marker \
             FROM main.zkill_statistics_cache \
             WHERE character_id IN ({ids});");

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;

        while let Some(row) = rows.next()? {
            let no_history_marker: Option<bool> = row.get(9)?;
            let snapshot = ZKillStatisticsSnapshot {
                character_id: row.get(0)?,
                ships_destroyed: row.get(1)?,
                solo_kills: row.get(2)?,
                solo_ratio: row.get(3)?,
                avg_gang_size: row.get(4)?,
                ships_lost: row.get(5)?,
                solo_losses: row.get(6)?,
                general_style: row.get(7)?,
                checked_at_utc: row.get(8)?,
                no_history_marker: no_history_marker.unwrap_or(false),
            };

            results.insert(snapshot.character_id, snapshot);
        }

        Ok(results)
    }

    pub fn get_for_character(
        &self,
        character_id: i64,
    ) -> RepositoryResult<Option<ZKillStatisticsSnapshot>> {
        let connection = open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc, no_history_marker \
             FROM main.zkill_statistics_cache \
             WHERE character_id = {} \
             LIMIT 1;",
            character_id);

        let mut statement = connection.prepare(&sql)?;

        let mut rows = statement.query([])?;

        if let Some(row) = rows.next()? {
            let no_history_marker: Option<bool> = row.get(9)?;

            return Ok(Some(ZKillStatisticsSnapshot {
                character_id: row.get(0)?,
                ships_destroyed: row.get(1)?,
                solo_kills: row.get(2)?,
                solo_ratio: row.get(3)?,
                avg_gang_size: row.get(4)?,
                ships_lost: row.get(5)?,
                solo_losses: row.get(6)?,
                general_style: row.get(7)?,
                checked_at_utc: row.get(8)?,
                no_history_marker: no_history_marker.unwrap_or(false),
            }));
        }

        Ok(None)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unique_suffix() -> u128 {
        static COUNTER: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();

        nanos * 1000 + u128::from(COUNTER.fetch_add(1, std::sync::atomic::Ordering::SeqCst) % 1000)
    }

    #[test]
    fn get_for_characters_on_matches_single_reads() {
        let path = std::env::temp_dir().join(format!("statistics-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        connection
            .execute_batch(
                "CREATE TABLE zkill_statistics_cache (
                    character_id BIGINT PRIMARY KEY,
                    ships_destroyed INTEGER NOT NULL,
                    solo_kills INTEGER NOT NULL,
                    solo_ratio DOUBLE NOT NULL,
                    avg_gang_size DOUBLE NOT NULL,
                    ships_lost INTEGER NOT NULL,
                    solo_losses INTEGER NOT NULL,
                    general_style TEXT NOT NULL,
                    checked_at_utc TEXT NOT NULL,
                    no_history_marker BOOLEAN
                );
                INSERT INTO zkill_statistics_cache VALUES
                    (95465499, 100, 20, 0.2, 4.5, 10, 1, 'Gang', '2026-09-20T00:00:00+00:00', FALSE),
                    (91321792, 5, 0, 0.0, 6.0, 1, 0, 'Fleet', '2026-09-20T00:00:00+00:00', NULL);",
            )
            .unwrap();

        let batch = ZKillStatisticsRepository::get_for_characters_on(&connection, &[95465499, 91321792, 5]).unwrap();
        drop(connection);

        let repository = ZKillStatisticsRepository::new(path.clone());
        let first = repository.get_for_character(95465499).unwrap().unwrap();
        let second = repository.get_for_character(91321792).unwrap().unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(batch[&95465499], first);
        assert_eq!(batch[&91321792], second);
        assert!(!batch.contains_key(&5));
    }
}
