use std::collections::HashSet;
use std::path::PathBuf;

use crate::repositories::duckdb_database::open_connection;
use crate::repositories::repository_error::RepositoryResult;

pub struct SdeNpcCorporationRepository {
    database_path: PathBuf,
}

impl SdeNpcCorporationRepository {
    pub fn new(database_path: PathBuf) -> Self {
        Self { database_path }
    }

    pub fn get_all_ids(&self) -> RepositoryResult<HashSet<i64>> {
        let connection = open_connection(&self.database_path)?;

        let mut statement = connection.prepare("SELECT corporation_id FROM main.sde_npc_corporations;")?;

        let mut rows = statement.query([])?;
        let mut ids = HashSet::new();

        while let Some(row) = rows.next()? {
            ids.insert(row.get(0)?);
        }

        Ok(ids)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use duckdb::Connection;

    fn create_schema(connection: &Connection) {
        connection
            .execute_batch("CREATE TABLE sde_npc_corporations (corporation_id BIGINT PRIMARY KEY);")
            .unwrap();
    }

    fn unique_suffix() -> u128 {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    }

    #[test]
    fn get_all_ids_returns_every_row() {
        let path = std::env::temp_dir().join(format!("sde-npc-corp-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);

        connection
            .execute_batch("INSERT INTO sde_npc_corporations VALUES (1000001), (1000002), (1000132);")
            .unwrap();
        drop(connection);

        let repository = SdeNpcCorporationRepository::new(path.clone());
        let ids = repository.get_all_ids().unwrap();

        std::fs::remove_file(&path).unwrap();

        assert_eq!(ids, HashSet::from([1000001, 1000002, 1000132]));
    }

    #[test]
    fn get_all_ids_empty_table_returns_empty_set() {
        let path = std::env::temp_dir().join(format!("sde-npc-corp-repo-{}.duckdb", unique_suffix()));
        let connection = Connection::open(&path).unwrap();
        create_schema(&connection);
        drop(connection);

        let repository = SdeNpcCorporationRepository::new(path.clone());
        let ids = repository.get_all_ids().unwrap();

        std::fs::remove_file(&path).unwrap();

        assert!(ids.is_empty());
    }
}
