use std::env;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde_json::{json, Value};

pub const LIVE_STATUS_FILE_NAME: &str = "groupHistory.status.json";
pub const SWAP_SIGNAL_FILE_NAME: &str = "database.new";

#[derive(Clone)]
pub struct GroupHistoryLiveConfig {
    pub active_database_file: String,
    pub schema_version: i32,
    pub last_completed_day_utc: Option<String>,
    pub last_updated_utc: Option<String>,
    pub update_in_progress: bool,
}

impl GroupHistoryLiveConfig {
    pub fn empty() -> Self {
        GroupHistoryLiveConfig {
            active_database_file: String::new(),
            schema_version: 0,
            last_completed_day_utc: None,
            last_updated_utc: None,
            update_in_progress: false,
        }
    }
}

pub fn get_live_config_directory() -> PathBuf {
    let local_app_data = env::var("LOCALAPPDATA").expect("LOCALAPPDATA environment variable is not set");
    let directory = PathBuf::from(local_app_data).join("KillRight").join("config");
    fs::create_dir_all(&directory).expect("failed to create KillRight config directory");
    directory
}

pub fn get_live_status_path(directory: &Path) -> PathBuf {
    directory.join(LIVE_STATUS_FILE_NAME)
}

pub fn get_swap_signal_path(directory: &Path) -> PathBuf {
    directory.join(SWAP_SIGNAL_FILE_NAME)
}

pub fn read_live_config(directory: &Path) -> GroupHistoryLiveConfig {
    let path = get_live_status_path(directory);

    let text = match fs::read_to_string(&path) {
        Ok(text) => text,
        Err(_) => return GroupHistoryLiveConfig::empty(),
    };

    let root: Value = match serde_json::from_str(&text) {
        Ok(value) => value,
        Err(_) => return GroupHistoryLiveConfig::empty(),
    };

    let group_history = &root["groupHistory"];

    GroupHistoryLiveConfig {
        active_database_file: group_history["activeDatabaseFile"].as_str().unwrap_or("").to_string(),
        schema_version: group_history["schemaVersion"].as_i64().unwrap_or(0) as i32,
        last_completed_day_utc: group_history["lastCompletedDayUtc"].as_str().map(|value| value.to_string()),
        last_updated_utc: group_history["lastUpdatedUtc"].as_str().map(|value| value.to_string()),
        update_in_progress: group_history["updateInProgress"].as_bool().unwrap_or(false),
    }
}

pub fn write_live_config(directory: &Path, config: &GroupHistoryLiveConfig) -> io::Result<()> {
    let path = get_live_status_path(directory);

    let root = json!({
        "groupHistory": {
            "activeDatabaseFile": config.active_database_file,
            "schemaVersion": config.schema_version,
            "lastCompletedDayUtc": config.last_completed_day_utc,
            "lastUpdatedUtc": config.last_updated_utc,
            "updateInProgress": config.update_in_progress,
        }
    });

    let text = serde_json::to_string_pretty(&root).expect("failed to serialize groupHistory live config");
    fs::write(path, text)
}

pub fn write_swap_signal(directory: &Path) -> io::Result<()> {
    fs::write(get_swap_signal_path(directory), b"")
}
