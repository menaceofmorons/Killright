use std::io::{self, Read};
use std::path::PathBuf;

use killright_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use killright_engine::recent_style::RecentStyleRequest;
use killright_engine::shared::settings_loader::load_engine_settings_from_path;

fn main() {
    let settings_path = std::env::args_os()
        .nth(1)
        .map(PathBuf::from)
        .expect("usage: killright_engine_cli <settings path> < request.json");
    let settings = load_engine_settings_from_path(&settings_path)
        .unwrap_or_else(|error| panic!("failed to load settings: {}", error));
    let mut input = String::new();
    io::stdin()
        .read_to_string(&mut input)
        .expect("failed to read stdin");
    if input.trim().is_empty() {
        return;
    }
    let request: RecentStyleRequest =
        serde_json::from_str(&input).expect("failed to parse analysis request");
    let result = analyze_recent_style(request, &settings.style);
    let output = serde_json::to_string(&result).expect("failed to serialize analysis result");
    println!("{}", output);
}
