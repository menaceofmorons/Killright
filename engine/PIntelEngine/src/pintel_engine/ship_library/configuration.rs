use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use crate::pintel_engine::ship_library::models::ShipClassificationConfiguration;

const CONFIG_RELATIVE_PATH: &str = "config/ship-classifications.json";
const ENGINE_CONFIG_RELATIVE_PATH: &str = "engine/PIntelEngine/config/ship-classifications.json";
const CONFIG_PATH_ENVIRONMENT_VARIABLE: &str = "PINTELENGINE_SHIP_CLASSIFICATIONS";

pub fn load_default_configuration() -> Result<ShipClassificationConfiguration, String> {
    for candidate in candidate_paths() {
        if candidate.exists() {
            return load_configuration(&candidate);
        }
    }

    Err("ship classification configuration file was not found".to_string())
}

pub fn load_configuration(path: &Path) -> Result<ShipClassificationConfiguration, String> {
    let text = fs::read_to_string(path)
        .map_err(|error| format!("failed to read {}: {}", path.display(), error))?;

    serde_json::from_str::<ShipClassificationConfiguration>(&text)
        .map_err(|error| format!("failed to parse {}: {}", path.display(), error))
}

fn candidate_paths() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if let Ok(path) = env::var(CONFIG_PATH_ENVIRONMENT_VARIABLE) {
        if !path.trim().is_empty() {
            candidates.push(PathBuf::from(path));
        }
    }

    if let Ok(current_dir) = env::current_dir() {
        add_candidates_from_base(&mut candidates, &current_dir);

        for ancestor in current_dir.ancestors() {
            add_candidates_from_base(&mut candidates, ancestor);
        }
    }

    if let Ok(executable_path) = env::current_exe() {
        if let Some(executable_dir) = executable_path.parent() {
            add_candidates_from_base(&mut candidates, executable_dir);

            for ancestor in executable_dir.ancestors() {
                add_candidates_from_base(&mut candidates, ancestor);
            }
        }
    }

    candidates
}

fn add_candidates_from_base(candidates: &mut Vec<PathBuf>, base: &Path) {
    candidates.push(base.join(CONFIG_RELATIVE_PATH));
    candidates.push(base.join(ENGINE_CONFIG_RELATIVE_PATH));
}