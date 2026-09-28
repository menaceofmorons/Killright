use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

use crate::group_analysis::group_detection_configuration::{
    validate_group_detection_configuration, GroupDetectionConfiguration,
};
use crate::shared::recent_window_configuration::{
    validate_recent_window_configuration, RecentWindowConfiguration,
};
use crate::shared::style_configuration::{validate_style_configuration, StyleConfiguration};
use crate::threat_analysis::threat_configuration_loader::validate_threat_configuration;
use crate::threat_analysis::threat_configuration_models::ThreatConfiguration;

pub const SETTINGS_PATH_ENVIRONMENT_VARIABLE: &str = "KILLRIGHT_SETTINGS_PATH";

#[derive(Debug, Clone, Deserialize)]
struct SettingsFile {
    #[serde(rename = "recentWindowDays")]
    recent_window_days: i64,
    threat: ThreatConfiguration,
    #[serde(rename = "groupDetection")]
    group_detection: GroupDetectionConfiguration,
    style: StyleConfiguration,
}

pub struct EngineSettings {
    pub recent_window: RecentWindowConfiguration,
    pub threat: ThreatConfiguration,
    pub group_detection: GroupDetectionConfiguration,
    pub style: StyleConfiguration,
}

pub fn get_settings_path() -> Result<PathBuf, String> {
    match env::var_os(SETTINGS_PATH_ENVIRONMENT_VARIABLE) {
        Some(value) if !value.is_empty() => Ok(PathBuf::from(value)),

        _ => Err(format!(
            "{} environment variable is not set",
            SETTINGS_PATH_ENVIRONMENT_VARIABLE
        )),
    }
}

pub fn load_engine_settings() -> Result<EngineSettings, String> {
    let path = get_settings_path()?;

    load_engine_settings_from_path(&path)
}

pub fn load_engine_settings_from_path(path: &Path) -> Result<EngineSettings, String> {
    let text = fs::read_to_string(path)
        .map_err(|error| format!("failed to read {}: {}", path.display(), error))?;

    let text = text.trim_start_matches('\u{feff}');

    let parsed = serde_json::from_str::<SettingsFile>(text)
        .map_err(|error| format!("failed to parse {}: {}", path.display(), error))?;

    let recent_window = RecentWindowConfiguration {
        recent_window_days: parsed.recent_window_days,
    };

    validate_recent_window_configuration(&recent_window).map_err(|error| {
        format!(
            "recent window configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    validate_threat_configuration(&parsed.threat).map_err(|error| {
        format!(
            "threat configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    validate_group_detection_configuration(&parsed.group_detection).map_err(|error| {
        format!(
            "group detection configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    validate_style_configuration(&parsed.style).map_err(|error| {
        format!(
            "style configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    Ok(EngineSettings {
        recent_window,
        threat: parsed.threat,
        group_detection: parsed.group_detection,
        style: parsed.style,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unique_suffix() -> u128 {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    }

    fn valid_settings_json() -> &'static str {
        r#"{
            "recentWindowDays": 14,
            "backupRotationCount": 5,
            "threat": {
                "version": "2.0",
                "lastUpdated": "2026-09-24",
                "componentWeights": {
                    "historicalCapability": {"maximumScore": 40},
                    "survivability": {"maximumScore": 25},
                    "lossQuality": {"maximumScore": 20},
                    "recentActivity": {"maximumScore": 10},
                    "securityStatus": {"maximumScore": 5}
                },
                "historicalCapability": {
                    "killVolumeBands": [{"maximumKills": 0, "score": 0}],
                    "soloKillBands": [{"maximumSoloKills": 0, "score": 0}],
                    "soloRatio": {"minimumRatio": 60.0, "score": 2},
                    "styleModifiers": [{"style": "Solo", "score": 4}]
                },
                "survivability": {
                    "ratios": [{"maximumRatio": 1.0, "score": 8}],
                    "noLossBands": [{"minimumKills": 1, "score": 10}]
                },
                "lossQuality": {
                    "styles": [{"style": "Solo", "bands": [{"minimumSoloLossRatio": 0.0, "score": 18}]}],
                    "defaultStyle": {"bands": [{"minimumSoloLossRatio": 0.0, "score": 16}]},
                    "noLossesWithKillsScore": 15
                },
                "recentActivity": {"points": [{"dailyRate": 0.0, "score": 0.0}, {"dailyRate": 1.0, "score": 10.0}]},
                "securityStatus": {"bands": [{"minimumSecurityStatus": 0.0, "score": 0}]}
            },
            "groupDetection": {
                "minimumSharedEvents": 2,
                "strengthStep": 10,
                "gangSizeWeights": [{"maximumGangSize": 3, "weight": 1.0}],
                "sampleFactor": {"minimum": 0.5, "maximum": 1.0, "saturatesAtCountedSharedKills": 10},
                "splitBonus": 20,
                "chainDiscount": 0.75,
                "intermediaryBonus": {"perAdditional": 10, "maximum": 30}
            },
            "style": {"blobMinimumAverageAttackers": 5, "fleetMinimumAverageAttackers": 11}
        }"#
    }

    #[test]
    fn load_engine_settings_from_path_parses_all_sections() {
        let path = std::env::temp_dir().join(format!("killright-settings-{}.json", unique_suffix()));
        fs::write(&path, valid_settings_json()).unwrap();

        let settings = load_engine_settings_from_path(&path).unwrap();

        fs::remove_file(&path).unwrap();
        assert_eq!(settings.recent_window.recent_window_days, 14);
        assert_eq!(settings.threat.component_weights.historical_capability.maximum_score, 40);
        assert_eq!(settings.group_detection.minimum_shared_events, 2);
        assert_eq!(settings.style.blob_minimum_average_attackers, 5.0);
    }

    #[test]
    fn load_engine_settings_from_path_ignores_unknown_top_level_keys() {
        let path = std::env::temp_dir().join(format!("killright-settings-unknown-{}.json", unique_suffix()));
        let json = valid_settings_json().replacen(
            "\"backupRotationCount\": 5,",
            "\"backupRotationCount\": 5, \"someFutureKey\": {\"nested\": true},",
            1,
        );
        fs::write(&path, json).unwrap();

        let result = load_engine_settings_from_path(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_ok());
    }

    #[test]
    fn load_engine_settings_from_path_rejects_invalid_recent_window() {
        let path = std::env::temp_dir().join(format!("killright-settings-invalid-window-{}.json", unique_suffix()));
        let json = valid_settings_json().replace("\"recentWindowDays\": 14,", "\"recentWindowDays\": 0,");
        fs::write(&path, json).unwrap();

        let result = load_engine_settings_from_path(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }

    #[test]
    fn load_engine_settings_from_path_rejects_invalid_style_boundaries() {
        let path = std::env::temp_dir().join(format!("killright-settings-invalid-style-{}.json", unique_suffix()));
        let json = valid_settings_json().replace(
            "\"style\": {\"blobMinimumAverageAttackers\": 5, \"fleetMinimumAverageAttackers\": 11}",
            "\"style\": {\"blobMinimumAverageAttackers\": 11, \"fleetMinimumAverageAttackers\": 11}",
        );
        fs::write(&path, json).unwrap();

        let result = load_engine_settings_from_path(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }
}
