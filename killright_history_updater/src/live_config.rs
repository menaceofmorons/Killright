use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde_json::{json, Value};

use crate::local_app_data::resolve_local_app_data_root;

pub const LIVE_STATUS_FILE_NAME: &str = "groupHistory.status.json";
pub const SWAP_SIGNAL_FILE_NAME: &str = "database.new";

#[derive(Clone)]
pub struct GroupHistoryLiveConfig {
    pub active_database_file: String,
    pub schema_version: i32,
    pub last_completed_day_utc: Option<String>,
    pub last_updated_utc: Option<String>,
    pub update_in_progress: bool,
    /// PID of the process that set `update_in_progress: true`, so a reader
    /// with no other way to know whether that process is still alive (for
    /// example a Developer window instance that did not launch it) can check
    /// for itself -- mirroring `SingleInstanceLock`'s own stale-PID handling
    /// in `lock.rs` (Step CC-08.08.26.01). Always `None` when
    /// `update_in_progress` is `false`.
    pub update_in_progress_pid: Option<u32>,
}

impl GroupHistoryLiveConfig {
    pub fn empty() -> Self {
        GroupHistoryLiveConfig {
            active_database_file: String::new(),
            schema_version: 0,
            last_completed_day_utc: None,
            last_updated_utc: None,
            update_in_progress: false,
            update_in_progress_pid: None,
        }
    }
}

pub fn get_live_config_directory() -> PathBuf {
    let local_app_data = resolve_local_app_data_root();
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
        update_in_progress_pid: group_history["updateInProgressPid"].as_u64().map(|value| value as u32),
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
            "updateInProgressPid": config.update_in_progress_pid,
        }
    });

    let text = serde_json::to_string_pretty(&root).expect("failed to serialize groupHistory live config");
    fs::write(path, text)
}

pub fn write_swap_signal(directory: &Path) -> io::Result<()> {
    fs::write(get_swap_signal_path(directory), b"")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn write_then_read_round_trips_update_in_progress_pid() {
        let directory = std::env::temp_dir().join("killright-live-config-test-round-trip");
        fs::create_dir_all(&directory).unwrap();

        let config = GroupHistoryLiveConfig {
            active_database_file: "C:\\example\\KillRight.History.260808.01.duckdb".to_string(),
            schema_version: 1,
            last_completed_day_utc: Some("2017-08-01".to_string()),
            last_updated_utc: Some("2026-08-08T10:00:00+00:00".to_string()),
            update_in_progress: true,
            update_in_progress_pid: Some(4242),
        };

        write_live_config(&directory, &config).unwrap();
        let read_back = read_live_config(&directory);

        assert_eq!(read_back.update_in_progress_pid, Some(4242));

        fs::remove_dir_all(&directory).ok();
    }

    #[test]
    fn read_live_config_defaults_update_in_progress_pid_to_none_when_absent_from_json() {
        let directory = std::env::temp_dir().join("killright-live-config-test-legacy");
        fs::create_dir_all(&directory).unwrap();

        // Simulates a live status file written by a pre-CC-08.08.26.01 build,
        // before updateInProgressPid existed.
        let legacy_json = r#"{
            "groupHistory": {
                "activeDatabaseFile": "C:\\example\\KillRight.History.260805.01.duckdb",
                "schemaVersion": 1,
                "lastCompletedDayUtc": "2017-07-31",
                "lastUpdatedUtc": "2026-08-07T17:30:04+00:00",
                "updateInProgress": true
            }
        }"#;
        fs::write(get_live_status_path(&directory), legacy_json).unwrap();

        let read_back = read_live_config(&directory);

        assert_eq!(read_back.update_in_progress_pid, None);

        fs::remove_dir_all(&directory).ok();
    }

    #[test]
    fn empty_config_has_no_update_in_progress_pid() {
        assert_eq!(GroupHistoryLiveConfig::empty().update_in_progress_pid, None);
    }
}
