use std::error::Error;
use std::fmt::{Display, Formatter};

#[derive(Debug)]
pub enum RepositoryError {
    MissingDatabasePath(String),
    DuckDb(duckdb::Error),
}

impl Display for RepositoryError {
    fn fmt(
        &self,
        formatter: &mut Formatter<'_>)
        -> std::fmt::Result
    {
        match self {
            RepositoryError::MissingDatabasePath(message) =>
                write!(formatter, "{}", message),

            RepositoryError::DuckDb(error) =>
                write!(formatter, "DuckDB error: {}", error),
        }
    }
}

impl Error for RepositoryError {}

impl From<duckdb::Error> for RepositoryError {
    fn from(error: duckdb::Error) -> Self {
        RepositoryError::DuckDb(error)
    }
}

pub type RepositoryResult<T> = Result<T, RepositoryError>;