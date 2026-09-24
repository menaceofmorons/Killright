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
    validate_non_negative_scores(configuration)?;
    validate_recent_activity_points(configuration)?;
    validate_security_status_bounds(configuration)?;

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

fn validate_recent_activity_points(configuration: &ThreatConfiguration) -> Result<(), String> {
    let points = &configuration.recent_activity.points;

    if points.is_empty() {
        return Err("recentActivity.points must not be empty".to_string());
    }

    let maximum_score = configuration.component_weights.recent_activity.maximum_score as f64;
    let mut previous_rate = f64::MIN;
    let mut previous_score = f64::MIN;

    for point in points {
        if point.daily_rate <= previous_rate {
            return Err("recentActivity.points must be strictly ordered by ascending dailyRate".to_string());
        }

        if point.score < previous_score {
            return Err("recentActivity.points scores must be non-decreasing by dailyRate".to_string());
        }

        if point.score < 0.0 || point.score > maximum_score {
            return Err(format!(
                "recentActivity.points score {} is outside the component maximum of {}",
                point.score, maximum_score
            ));
        }

        previous_rate = point.daily_rate;
        previous_score = point.score;
    }

    Ok(())
}

fn validate_security_status_bounds(configuration: &ThreatConfiguration) -> Result<(), String> {
    let maximum_score = configuration.component_weights.security_status.maximum_score;

    for band in &configuration.security_status.bands {
        if band.score < 0 || band.score > maximum_score {
            return Err(format!(
                "securityStatus band score {} is outside the component maximum of {}",
                band.score, maximum_score
            ));
        }
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
