use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct GroupDetectionConfiguration {
    #[serde(rename = "minimumSharedEvents")]
    pub minimum_shared_events: i64,
    #[serde(rename = "npcCorporationIdThreshold")]
    pub npc_corporation_id_threshold: i64,
    #[serde(rename = "genericNpcCorporationIds")]
    pub generic_npc_corporation_ids: Option<Vec<i64>>,
    #[serde(rename = "strengthStep")]
    pub strength_step: i32,
    #[serde(rename = "gangSizeWeights")]
    pub gang_size_weights: Vec<GangSizeWeight>,
    #[serde(rename = "sampleFactor")]
    pub sample_factor: SampleFactorConfiguration,
    #[serde(rename = "splitBonus")]
    pub split_bonus: i32,
    #[serde(rename = "chainDiscount")]
    pub chain_discount: f64,
    #[serde(rename = "intermediaryBonus")]
    pub intermediary_bonus: IntermediaryBonusConfiguration,
}

#[derive(Debug, Clone, Deserialize)]
pub struct GangSizeWeight {
    #[serde(rename = "maximumGangSize")]
    pub maximum_gang_size: i64,
    pub weight: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SampleFactorConfiguration {
    pub minimum: f64,
    pub maximum: f64,
    #[serde(rename = "saturatesAtCountedSharedKills")]
    pub saturates_at_counted_shared_kills: i64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct IntermediaryBonusConfiguration {
    #[serde(rename = "perAdditional")]
    pub per_additional: i32,
    pub maximum: i32,
}

pub fn load_default_group_detection_configuration() -> Result<GroupDetectionConfiguration, String> {
    for candidate in configuration_candidates() {
        if candidate.exists() {
            return load_group_detection_configuration(&candidate);
        }
    }

    Err("group-detection.json was not found".to_string())
}

pub fn load_group_detection_configuration(path: &Path) -> Result<GroupDetectionConfiguration, String> {
    let text = fs::read_to_string(path)
        .map_err(|error| format!("failed to read {}: {}", path.display(), error))?;

    let text = text.trim_start_matches('\u{feff}');

    let configuration = serde_json::from_str::<GroupDetectionConfiguration>(text)
        .map_err(|error| format!("failed to parse {}: {}", path.display(), error))?;

    validate_group_detection_configuration(&configuration).map_err(|error| {
        format!(
            "group detection configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    Ok(configuration)
}

fn configuration_candidates() -> Vec<PathBuf> {
    let current_directory = env::current_dir().unwrap_or_else(|_| PathBuf::from("."));

    let mut candidates = vec![
        current_directory.join("config").join("group-detection.json"),
        current_directory
            .join("engine")
            .join("PIntelEngine")
            .join("config")
            .join("group-detection.json"),
    ];

    if let Ok(exe_path) = env::current_exe() {
        if let Some(exe_directory) = exe_path.parent() {
            candidates.push(exe_directory.join("config").join("group-detection.json"));
        }
    }

    candidates
}

fn validate_group_detection_configuration(configuration: &GroupDetectionConfiguration) -> Result<(), String> {
    validate_npc_corporation_configuration(configuration)?;
    validate_weights_and_factors(configuration)?;
    validate_steps_and_saturation(configuration)?;

    Ok(())
}

fn validate_npc_corporation_configuration(configuration: &GroupDetectionConfiguration) -> Result<(), String> {
    if configuration.npc_corporation_id_threshold <= 0 {
        return Err(format!(
            "npcCorporationIdThreshold must be positive; actual value was {}",
            configuration.npc_corporation_id_threshold
        ));
    }

    if let Some(generic_ids) = &configuration.generic_npc_corporation_ids {
        if generic_ids.iter().any(|id| *id <= 0) {
            return Err("genericNpcCorporationIds cannot contain a non-positive corporation id".to_string());
        }
    }

    Ok(())
}

fn validate_weights_and_factors(configuration: &GroupDetectionConfiguration) -> Result<(), String> {
    for gang_size_weight in &configuration.gang_size_weights {
        if !(0.0..=1.0).contains(&gang_size_weight.weight) {
            return Err(format!(
                "gangSizeWeights entry for maximumGangSize {} has weight {} outside 0-1",
                gang_size_weight.maximum_gang_size, gang_size_weight.weight
            ));
        }
    }

    let sample_factor = &configuration.sample_factor;

    if !(0.0..=1.0).contains(&sample_factor.minimum) || !(0.0..=1.0).contains(&sample_factor.maximum) {
        return Err(format!(
            "sampleFactor minimum {} and maximum {} must both be within 0-1",
            sample_factor.minimum, sample_factor.maximum
        ));
    }

    if sample_factor.minimum > sample_factor.maximum {
        return Err(format!(
            "sampleFactor minimum {} cannot exceed maximum {}",
            sample_factor.minimum, sample_factor.maximum
        ));
    }

    if !(0.0..=1.0).contains(&configuration.chain_discount) {
        return Err(format!(
            "chainDiscount {} must be within 0-1",
            configuration.chain_discount
        ));
    }

    Ok(())
}

fn validate_steps_and_saturation(configuration: &GroupDetectionConfiguration) -> Result<(), String> {
    if configuration.strength_step <= 0 {
        return Err(format!(
            "strengthStep must be positive; actual value was {}",
            configuration.strength_step
        ));
    }

    if configuration.sample_factor.saturates_at_counted_shared_kills <= 0 {
        return Err(format!(
            "sampleFactor.saturatesAtCountedSharedKills must be positive; actual value was {}",
            configuration.sample_factor.saturates_at_counted_shared_kills
        ));
    }

    if configuration.minimum_shared_events <= 0 {
        return Err(format!(
            "minimumSharedEvents must be positive; actual value was {}",
            configuration.minimum_shared_events
        ));
    }

    Ok(())
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

    fn valid_configuration_json() -> &'static str {
        r#"{
            "minimumSharedEvents": 2,
            "npcCorporationIdThreshold": 1005000,
            "genericNpcCorporationIds": null,
            "strengthStep": 10,
            "gangSizeWeights": [
                {"maximumGangSize": 3, "weight": 1.0},
                {"maximumGangSize": 5, "weight": 0.75},
                {"maximumGangSize": 7, "weight": 0.5},
                {"maximumGangSize": 10, "weight": 0.25}
            ],
            "sampleFactor": {"minimum": 0.5, "maximum": 1.0, "saturatesAtCountedSharedKills": 10},
            "splitBonus": 20,
            "chainDiscount": 0.75,
            "intermediaryBonus": {"perAdditional": 10, "maximum": 30}
        }"#
    }

    #[test]
    fn load_group_detection_configuration_parses_valid_file() {
        let path = std::env::temp_dir().join(format!("group-detection-{}.json", unique_suffix()));
        fs::write(&path, valid_configuration_json()).unwrap();

        let configuration = load_group_detection_configuration(&path).unwrap();

        fs::remove_file(&path).unwrap();
        assert_eq!(configuration.minimum_shared_events, 2);
        assert_eq!(configuration.gang_size_weights.len(), 4);
    }

    #[test]
    fn load_group_detection_configuration_rejects_weight_outside_zero_one() {
        let path = std::env::temp_dir().join(format!("group-detection-invalid-weight-{}.json", unique_suffix()));
        let json = valid_configuration_json().replace("\"weight\": 1.0", "\"weight\": 1.5");
        fs::write(&path, json).unwrap();

        let result = load_group_detection_configuration(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }

    #[test]
    fn load_group_detection_configuration_rejects_non_positive_npc_threshold() {
        let path = std::env::temp_dir().join(format!("group-detection-invalid-npc-{}.json", unique_suffix()));
        let json = valid_configuration_json().replace("\"npcCorporationIdThreshold\": 1005000", "\"npcCorporationIdThreshold\": 0");
        fs::write(&path, json).unwrap();

        let result = load_group_detection_configuration(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }

    #[test]
    fn load_group_detection_configuration_rejects_non_positive_saturation() {
        let path = std::env::temp_dir().join(format!("group-detection-invalid-saturation-{}.json", unique_suffix()));
        let json = valid_configuration_json().replace("\"saturatesAtCountedSharedKills\": 10", "\"saturatesAtCountedSharedKills\": 0");
        fs::write(&path, json).unwrap();

        let result = load_group_detection_configuration(&path);

        fs::remove_file(&path).unwrap();
        assert!(result.is_err());
    }
}
