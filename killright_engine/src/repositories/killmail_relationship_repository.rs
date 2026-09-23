use std::path::PathBuf;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

#[derive(Debug, Clone, PartialEq)]
pub struct KillmailAttackerEvidence {
    pub killmail_id: i64,
    pub character_id: i64,
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub kill_time_utc: String,
}

pub struct KillmailRelationshipRepository {
    database_path: PathBuf,
}

impl KillmailRelationshipRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_attacker_evidence_for_scan_set(
        &self,
        character_ids: &[i64],
    ) -> RepositoryResult<Vec<KillmailAttackerEvidence>> {
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
            "SELECT a.killmail_id, a.character_id, a.corporation_id, a.alliance_id, k.kill_time_utc \
             FROM main.zkill_killmail_attackers a \
             JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id \
             WHERE a.character_id IN ({ids}) \
             ORDER BY a.killmail_id, k.kill_time_utc;");

        let mut statement = connection.prepare(&sql)?;

        let rows = statement.query_map([], |row| {
            Ok(KillmailAttackerEvidence {
                killmail_id: row.get(0)?,
                character_id: row.get(1)?,
                corporation_id: row.get(2)?,
                alliance_id: row.get(3)?,
                kill_time_utc: row.get(4)?,
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
    fn get_attacker_evidence_for_scan_set_returns_rows_for_requested_characters_only() {
        let path = std::env::temp_dir().join(format!("killmail-relationship-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection.execute_batch(
            "INSERT INTO zkill_killmails VALUES
                (1, 'hash1', '2026-09-20T00:00:00+00:00', 30000142, 40000001, 999, 587, 3, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00');
            INSERT INTO zkill_killmail_attackers VALUES
                (1, 95465499, 98000001, NULL, 11567),
                (1, 91321792, 98000002, 99000001, 17738),
                (1, 90000003, 98000003, NULL, 670);",
        )
        .unwrap();
        drop(connection);

        let repository = KillmailRelationshipRepository::new(path.clone());
        let rows = repository
            .get_attacker_evidence_for_scan_set(&[95465499, 91321792])
            .unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(rows.len(), 2);
        assert!(rows.iter().any(|row| row.character_id == 95465499));
        assert!(rows.iter().any(|row| row.character_id == 91321792));
        assert!(!rows.iter().any(|row| row.character_id == 90000003));
    }

    #[test]
    fn get_attacker_evidence_for_scan_set_empty_scan_set_returns_empty() {
        let path = std::env::temp_dir().join(format!("killmail-relationship-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);
        drop(connection);

        let repository = KillmailRelationshipRepository::new(path.clone());
        let rows = repository.get_attacker_evidence_for_scan_set(&[]).unwrap();

        std::fs::remove_file(&path).unwrap();

        assert!(rows.is_empty());
    }
}
