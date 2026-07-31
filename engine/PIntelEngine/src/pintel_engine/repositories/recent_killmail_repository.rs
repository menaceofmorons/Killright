use std::path::PathBuf;

use crate::pintel_engine::repositories::duckdb_database::open_connection;
use crate::pintel_engine::repositories::repository_error::RepositoryResult;

#[derive(Debug, Clone, PartialEq)]
pub struct RecentKillmailSnapshot {
    pub killmail_id: i64,
    pub killmail_hash: Option<String>,
    pub character_id: i64,
    pub kill_time_utc: String,
    pub is_loss: bool,
    pub attacker_count: i32,
    pub is_solo: bool,
    pub ship_type_id: Option<i64>,
    pub system_id: Option<i64>,
    pub location_id: Option<i64>,
    pub is_npc: bool,
    pub cached_at_utc: String,
}

pub struct RecentKillmailRepository {
    database_path: PathBuf,
}

impl RecentKillmailRepository {
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
        -> RepositoryResult<Vec<RecentKillmailSnapshot>>
    {
        let connection =
            open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT killmail_id, killmail_hash, character_id, kill_time_utc, is_loss, attacker_count, is_solo, ship_type_id, system_id, location_id, is_npc, cached_at_utc \
             FROM main.zkill_recent_killmail_cache \
             WHERE character_id = {} \
             ORDER BY kill_time_utc DESC;",
            character_id);

        let mut statement =
            connection.prepare(&sql)?;

        let rows =
            statement.query_map([], |row| {
                Ok(RecentKillmailSnapshot {
                    killmail_id: row.get(0)?,
                    killmail_hash: row.get(1)?,
                    character_id: row.get(2)?,
                    kill_time_utc: row.get(3)?,
                    is_loss: row.get(4)?,
                    attacker_count: row.get(5)?,
                    is_solo: row.get(6)?,
                    ship_type_id: row.get(7)?,
                    system_id: row.get(8)?,
                    location_id: row.get(9)?,
                    is_npc: row.get(10)?,
                    cached_at_utc: row.get(11)?,
                })
            })?;

        let mut results =
            Vec::new();

        for row in rows {
            results.push(row?);
        }

        Ok(results)
    }
}