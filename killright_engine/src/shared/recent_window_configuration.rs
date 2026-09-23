use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct RecentWindowConfiguration {
    #[serde(rename = "recentWindowDays")]
    pub recent_window_days: i64,
}

pub fn load_default_recent_window_configuration() -> Result<RecentWindowConfiguration, String> {
    for candidate in configuration_candidates() {
        if candidate.exists() {
            return load_recent_window_configuration(&candidate);
        }
    }

    Err("recent-window.json was not found".to_string())
}

pub fn load_recent_window_configuration(path: &Path) -> Result<RecentWindowConfiguration, String> {
    let text = fs::read_to_string(path)
        .map_err(|error| format!("failed to read {}: {}", path.display(), error))?;

    let text = text.trim_start_matches('\u{feff}');

    let configuration = serde_json::from_str::<RecentWindowConfiguration>(text)
        .map_err(|error| format!("failed to parse {}: {}", path.display(), error))?;

    validate_recent_window_configuration(&configuration).map_err(|error| {
        format!(
            "recent window configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    Ok(configuration)
}

fn configuration_candidates() -> Vec<PathBuf> {
    let current_directory = env::current_dir().unwrap_or_else(|_| PathBuf::from("."));

    let mut candidates = vec![
        current_directory.join("config").join("recent-window.json"),
        current_directory
            .join("engine")
            .join("PIntelEngine")
            .join("config")
            .join("recent-window.json"),
    ];

    if let Ok(exe_path) = env::current_exe() {
        if let Some(exe_directory) = exe_path.parent() {
            candidates.push(exe_directory.join("config").join("recent-window.json"));
        }
    }

    candidates
}

fn validate_recent_window_configuration(configuration: &RecentWindowConfiguration) -> Result<(), String> {
    if configuration.recent_window_days <= 0 {
        return Err(format!(
            "recentWindowDays must be positive; actual value was {}",
            configuration.recent_window_days
        ));
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    #[test]
    fn load_recent_window_configuration_parses_valid_file() {
        let mut path = std::env::temp_dir();
        path.push(format!("recent-window-{}.json", std::process::id()));
        let mut file = fs::File::create(&path).unwrap();
        write!(file, r#"{{"recentWindowDays": 14}}"#).unwrap();

        let configuration = load_recent_window_configuration(&path).unwrap();

        fs::remove_file(&path).unwrap();
        assert_eq!(configuration.recent_window_days, 14);
    }

    #[test]
    fn load_recent_window_configuration_rejects_non_positive_window() {
        let mut path = std::env::temp_dir();
        path.push(format!("recent-window-invalid-{}.json", std::process::id()));
        let mut file = fs::File::create(&path).unwrap();
        write!(file, r#"{{"recentWindowDays": 0}}"#).unwrap();

        let result = load_recent_window_configuration(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }
}
