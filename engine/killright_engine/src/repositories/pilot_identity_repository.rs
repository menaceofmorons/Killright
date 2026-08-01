use std::path::PathBuf;

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
                cached_at_utc: row.get(11)?,
            }));
        }

        Ok(None)
    }
}
