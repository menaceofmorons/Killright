use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

use killright_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use killright_engine::recent_style::RecentStyleRequest;
use killright_engine::shared::settings_loader::load_engine_settings_from_path;

const SETTINGS_TEMPLATE: &str = r#"{
    "recentWindowDays": 14,
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
        "securityStatus": {"bands": [{"belowSecurityStatus": 0.0, "score": 1}]},
        "gating": {"mediumCap": 40, "highCap": 60, "mediumRatioMaximum": 0.5, "highRatioBelow": 1.0, "floor": 1}
    },
    "groupDetection": {
        "minimumSharedEvents": 2,
        "strengthStep": 10,
        "gangSizeWeights": [{"maximumGangSize": 3, "weight": 1.0}],
        "sampleFactor": {"minimum": 0.5, "maximum": 1.0, "saturatesAtCountedSharedKills": 10},
        "splitBonus": 20,
        "chainDiscount": 0.75,
        "chainStrengthMaximum": 75,
        "intermediaryBonus": {"perAdditional": 10, "maximum": 30}
    },
    "style": {"blobMinimumAverageAttackers": BLOB, "fleetMinimumAverageAttackers": 11, "podderMinimumSharePercent": 35, "podderMinimumKillCount": 5}
}"#;

const REQUEST: &str = r#"{"character_id":42,"killmails":[
    {"killmail_id":1,"is_loss":false,"attacker_count":6,"is_solo":false,"ship_type_id":null},
    {"killmail_id":2,"is_loss":false,"attacker_count":6,"is_solo":false,"ship_type_id":null},
    {"killmail_id":3,"is_loss":false,"attacker_count":6,"is_solo":false,"ship_type_id":null}
]}"#;

fn write_settings(label: &str, blob_minimum: u32) -> PathBuf {
    let path = std::env::temp_dir().join(format!(
        "killright-cli-{}-{}-{}.json",
        label,
        std::process::id(),
        blob_minimum
    ));
    std::fs::write(
        &path,
        SETTINGS_TEMPLATE.replace("BLOB", &blob_minimum.to_string()),
    )
    .unwrap();
    path
}

fn run_cli(settings: &Path) -> String {
    let mut child = Command::new(env!("CARGO_BIN_EXE_killright_engine_cli"))
        .arg(settings)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(REQUEST.as_bytes())
        .unwrap();
    let output = child.wait_with_output().unwrap();
    assert!(output.status.success());
    String::from_utf8(output.stdout).unwrap().trim().to_string()
}

fn library_output(settings: &Path) -> String {
    let loaded = load_engine_settings_from_path(settings).unwrap();
    let request: RecentStyleRequest = serde_json::from_str(REQUEST).unwrap();
    serde_json::to_string(&analyze_recent_style(request, &loaded.style)).unwrap()
}

#[test]
fn cli_output_matches_library_for_sample_pilot() {
    let settings = write_settings("match", 5);

    let cli = run_cli(&settings);
    let library = library_output(&settings);

    std::fs::remove_file(&settings).unwrap();
    assert_eq!(cli, library);
}

#[test]
fn cli_classification_follows_threshold_in_settings_file() {
    let low = write_settings("low", 5);
    let high = write_settings("high", 7);

    let at_low = run_cli(&low);
    let at_high = run_cli(&high);

    let library_low = library_output(&low);
    let library_high = library_output(&high);

    std::fs::remove_file(&low).unwrap();
    std::fs::remove_file(&high).unwrap();
    assert_ne!(at_low, at_high);
    assert_eq!(at_low, library_low);
    assert_eq!(at_high, library_high);
}

#[test]
fn cli_fails_without_settings_path() {
    let output = Command::new(env!("CARGO_BIN_EXE_killright_engine_cli"))
        .stdin(Stdio::null())
        .output()
        .unwrap();

    assert!(!output.status.success());
}
