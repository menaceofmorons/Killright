use std::fs;
use std::path::PathBuf;

use crate::local_app_data::resolve_local_app_data_root;

pub const HISTORY_UPDATER_DATABASE_FILE_NAME: &str = "KillRight.HistoryUpdater.duckdb";
pub const HISTORY_UPDATER_LOCK_FILE_NAME: &str = "history_update.lock";

pub fn get_default_database_path() -> PathBuf {
    get_history_updater_directory().join(HISTORY_UPDATER_DATABASE_FILE_NAME)
}

pub fn get_default_lock_path() -> PathBuf {
    get_history_updater_directory().join(HISTORY_UPDATER_LOCK_FILE_NAME)
}

pub fn get_history_updater_directory() -> PathBuf {
    let local_app_data = resolve_local_app_data_root();
    let directory = PathBuf::from(local_app_data)
        .join("KillRight")
        .join("HistoryUpdater");
    fs::create_dir_all(&directory).expect("failed to create KillRight HistoryUpdater directory");
    directory
}
