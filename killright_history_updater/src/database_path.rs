use std::fs;
use std::path::PathBuf;

use crate::folder_layout;
use crate::local_app_data::resolve_local_app_data_root;

pub const HISTORY_UPDATER_DATABASE_FILE_NAME: &str = "KillRight.HistoryUpdater.DevScratch.duckdb";
pub const HISTORY_UPDATER_LOCK_FILE_NAME: &str = "history_update.lock";

/// Step 19.01.09: relocated into `folder_layout::test_dir` (the `Test`
/// subfolder) and renamed from `KillRight.HistoryUpdater.duckdb` to
/// `...DevScratch.duckdb`, both deliberately -- this file was twice mistaken
/// for the real promoted Live database while it sat unlabeled at the
/// historic database root (the `Live`/`Working`/`Archive`/`Failed` layout,
/// `folder_layout.rs`, established at 19.00.55). Every existing caller
/// (`create-schema`, `import-day`, `rebuild-summary`, `print-*`,
/// `persist-classification`) is unaffected beyond the new location -- none
/// referenced the old bare path directly, all go through this function.
pub fn get_default_database_path() -> PathBuf {
    let test_directory = folder_layout::test_dir(&get_history_updater_directory());
    fs::create_dir_all(&test_directory).expect("failed to create KillRight HistoryUpdater Test directory");
    test_directory.join(HISTORY_UPDATER_DATABASE_FILE_NAME)
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
