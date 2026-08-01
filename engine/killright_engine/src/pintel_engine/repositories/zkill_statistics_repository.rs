use std::path::PathBuf;

use crate::pintel_engine::repositories::duckdb_database::open_connection;
use crate::pintel_engine::repositories::repository_error::RepositoryResult;

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
}

pub struct ZKillStatisticsRepository {
    database_path: PathBuf,
}

impl ZKillStatisticsRepository {
    pub fn new(
        database_path: PathBuf)
        -> Self
    {
        Self {
            database_path,
        }
    }

    pub fn get_for_character(
        &self,
        character_id: i64)
        -> RepositoryResult<Option<ZKillStatisticsSnapshot>>
    {
        let connection =
            open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc \
             FROM main.zkill_statistics_cache \
             WHERE character_id = {} \
             LIMIT 1;",
            character_id);

        let mut statement =
            connection.prepare(&sql)?;

        let mut rows =
            statement.query([])?;

        if let Some(row) = rows.next()? {
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
            }));
        }

        Ok(None)
    }
}