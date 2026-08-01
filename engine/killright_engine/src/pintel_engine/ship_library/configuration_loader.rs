use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use crate::pintel_engine::ship_library::ship_classification_models::ShipClassificationConfiguration;

pub fn load_default_configuration()
    -> Result<ShipClassificationConfiguration, String>
{
    let candidates =
        configuration_candidates();

    for candidate in candidates {

        if candidate.exists() {

            return load_configuration(&candidate);
        }
    }

    Err(
        "ship classification configuration file was not found"
            .to_string())
}

pub fn load_configuration(
    path: &Path)
    -> Result<ShipClassificationConfiguration, String>
{
    let text =
        fs::read_to_string(path)
            .map_err(|error|
                format!(
                    "failed to read {}: {}",
                    path.display(),
                    error))?;

    let text =
        text.trim_start_matches('\u{feff}');

    let configuration =
        serde_json::from_str::<ShipClassificationConfiguration>(text)
            .map_err(|error|
                format!(
                    "failed to parse {}: {}",
                    path.display(),
                    error))?;

    Ok(configuration)
}

fn configuration_candidates()
    -> Vec<PathBuf>
{
    let current_directory =
        env::current_dir()
            .unwrap_or_else(|_| PathBuf::from("."));

    vec![
        current_directory
            .join("config")
            .join("ship-classifications.json"),

        current_directory
            .join("engine")
            .join("PIntelEngine")
            .join("config")
            .join("ship-classifications.json"),
    ]
}