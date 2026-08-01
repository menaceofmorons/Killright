use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use crate::threat_analysis::threat_configuration_models::ThreatConfiguration;

pub fn load_default_threat_configuration() -> Result<ThreatConfiguration, String> {
    for candidate in configuration_candidates() {
        if candidate.exists() {
            return load_threat_configuration(&candidate);
        }
    }

    Err("threat-analysis.json was not found".to_string())
}

pub fn load_threat_configuration(path: &Path) -> Result<ThreatConfiguration, String> {
    let text = fs::read_to_string(path)
        .map_err(|error| format!("failed to read {}: {}", path.display(), error))?;

    let text = text.trim_start_matches('\u{feff}');

    let configuration = serde_json::from_str::<ThreatConfiguration>(text)
        .map_err(|error| format!("failed to parse {}: {}", path.display(), error))?;

    validate_threat_configuration(&configuration).map_err(|error| {
        format!(
            "threat configuration validation failed for {}: {}",
            path.display(),
            error
        )
    })?;

    Ok(configuration)
}

fn configuration_candidates() -> Vec<PathBuf> {
    let current_directory = env::current_dir().unwrap_or_else(|_| PathBuf::from("."));

    let mut candidates = vec![
        current_directory
            .join("config")
            .join("threat-analysis.json"),
        current_directory
            .join("engine")
            .join("PIntelEngine")
            .join("config")
            .join("threat-analysis.json"),
    ];

    if let Ok(exe_path) = env::current_exe() {
        if let Some(exe_directory) = exe_path.parent() {
            candidates.push(exe_directory.join("config").join("threat-analysis.json"));
        }
    }

    candidates
}

fn validate_threat_configuration(configuration: &ThreatConfiguration) -> Result<(), String> {
    validate_component_weights(configuration)?;
    validate_threat_bands(configuration)?;
    validate_non_negative_scores(configuration)?;

    Ok(())
}

fn validate_component_weights(configuration: &ThreatConfiguration) -> Result<(), String> {
    let weights = &configuration.component_weights;

    let total = weights.historical_capability.maximum_score
        + weights.survivability.maximum_score
        + weights.loss_quality.maximum_score
        + weights.recent_activity.maximum_score
        + weights.security_status.maximum_score;

    if total != 100 {
        return Err(format!(
            "component weights must total 100; actual total was {}",
            total
        ));
    }

    Ok(())
}

fn validate_threat_bands(configuration: &ThreatConfiguration) -> Result<(), String> {
    let mut bands = configuration.bands.clone();

    bands.sort_by_key(|band| band.minimum_score);

    let mut expected_minimum = 0;

    for band in bands {
        if band.minimum_score != expected_minimum {
            return Err(format!(
                "threat band '{}' starts at {}, expected {}",
                band.name, band.minimum_score, expected_minimum
            ));
        }

        if band.maximum_score < band.minimum_score {
            return Err(format!(
                "threat band '{}' maximum is below minimum",
                band.name
            ));
        }

        expected_minimum = band.maximum_score + 1;
    }

    if expected_minimum != 101 {
        return Err("threat bands must cover score range 0-100".to_string());
    }

    Ok(())
}

fn validate_non_negative_scores(configuration: &ThreatConfiguration) -> Result<(), String> {
    let weights = &configuration.component_weights;

    let all_weights = [
        weights.historical_capability.maximum_score,
        weights.survivability.maximum_score,
        weights.loss_quality.maximum_score,
        weights.recent_activity.maximum_score,
        weights.security_status.maximum_score,
    ];

    if all_weights.iter().any(|score| *score < 0) {
        return Err("component weights cannot be negative".to_string());
    }

    Ok(())
}
