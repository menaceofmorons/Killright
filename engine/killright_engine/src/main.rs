use std::io::{self, Read};
#[path = "kr_engine.rs"]
mod kr_engine;
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
pub use kr_engine::*;
fn main() {
    let mut input = String::new();
    io::stdin()
        .read_to_string(&mut input)
        .expect("failed to read stdin");
    if input.trim().is_empty() {
        return;
    }
    let request: RecentStyleRequest =
        serde_json::from_str(&input).expect("failed to parse analysis request");
    let result = analyze_recent_style(request);
    let output = serde_json::to_string(&result).expect("failed to serialize analysis result");
    println!("{}", output);
}
