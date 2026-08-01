use std::env;
use std::path::PathBuf;

pub const DATABASE_PATH_ENVIRONMENT_VARIABLE: &str = "PILOTINTEL_DB_PATH";

pub fn get_database_path()
    -> Result<PathBuf, String>
{
    match env::var_os(DATABASE_PATH_ENVIRONMENT_VARIABLE) {
        Some(value) if !value.is_empty() =>
            Ok(PathBuf::from(value)),

        _ =>
            Err(format!(
                "{} environment variable is not set",
                DATABASE_PATH_ENVIRONMENT_VARIABLE)),
    }
}

pub fn get_database_path_or_none()
    -> Option<PathBuf>
{
    get_database_path().ok()
}