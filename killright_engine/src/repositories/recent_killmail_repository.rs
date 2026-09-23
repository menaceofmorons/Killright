use std::path::PathBuf;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

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
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_for_character(
        &self,
        character_id: i64,
    ) -> RepositoryResult<Vec<RecentKillmailSnapshot>> {
        let connection = open_connection(&self.database_path)?;

        let sql = format!(
            "SELECT killmail_id, killmail_hash, {character_id} AS character_id, kill_time_utc, \
             TRUE AS is_loss, unique_attacker_count, is_solo, victim_ship_type_id, system_id, \
             location_id, is_npc, cached_at_utc \
             FROM main.zkill_killmails \
             WHERE victim_character_id = {character_id} \
             UNION ALL \
             SELECT k.killmail_id, k.killmail_hash, {character_id} AS character_id, k.kill_time_utc, \
             FALSE AS is_loss, k.unique_attacker_count, k.is_solo, NULL AS ship_type_id, k.system_id, \
             k.location_id, k.is_npc, k.cached_at_utc \
             FROM main.zkill_killmail_attackers a \
             JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id \
             WHERE a.character_id = {character_id} \
             ORDER BY kill_time_utc DESC;");

        let mut statement = connection.prepare(&sql)?;

        let rows = statement.query_map([], |row| {
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

        let mut results = Vec::new();

        for row in rows {
            results.push(row?);
        }

        Ok(results)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use duckdb::Connection;

    fn create_schema(connection: &Connection) {
        connection.execute_batch(
            "CREATE TABLE zkill_killmails (
                killmail_id BIGINT PRIMARY KEY,
                killmail_hash TEXT,
                kill_time_utc TEXT NOT NULL,
                system_id BIGINT NOT NULL,
                location_id BIGINT,
                victim_character_id BIGINT,
                victim_ship_type_id BIGINT,
                unique_attacker_count INTEGER NOT NULL,
                is_solo BOOLEAN NOT NULL,
                is_npc BOOLEAN NOT NULL,
                is_qualifying BOOLEAN NOT NULL,
                cached_at_utc TEXT NOT NULL
            );
            CREATE TABLE zkill_killmail_attackers (
                killmail_id BIGINT NOT NULL,
                character_id BIGINT NOT NULL,
                corporation_id BIGINT,
                alliance_id BIGINT,
                ship_type_id BIGINT,
                PRIMARY KEY (killmail_id, character_id)
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
    fn get_for_character_returns_kills_and_losses() {
        let path = std::env::temp_dir().join(format!("recent-killmail-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection.execute_batch(
            "INSERT INTO zkill_killmails VALUES
                (1, 'hash1', '2026-09-20T00:00:00+00:00', 30000142, 40000001, 91321792, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00'),
                (2, 'hash2', '2026-09-21T00:00:00+00:00', 30000142, 40000001, 95465499, 670, 3, FALSE, FALSE, TRUE, '2026-09-21T00:00:00+00:00');
            INSERT INTO zkill_killmail_attackers VALUES
                (1, 95465499, 98000001, NULL, 11567);",
        )
        .unwrap();
        drop(connection);

        let repository = RecentKillmailRepository::new(path.clone());
        let rows = repository.get_for_character(95465499).unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(rows.len(), 2);
        assert!(rows.iter().any(|row| row.killmail_id == 1 && !row.is_loss));
        assert!(rows.iter().any(|row| row.killmail_id == 2 && row.is_loss));
    }
}
