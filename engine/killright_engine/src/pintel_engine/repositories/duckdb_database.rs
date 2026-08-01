use std::path::Path;

use duckdb::Connection;

use crate::pintel_engine::repositories::repository_error::RepositoryResult;

pub fn open_connection(
    database_path: &Path)
    -> RepositoryResult<Connection>
{
    let connection = Connection::open(database_path)?;

    Ok(connection)
}