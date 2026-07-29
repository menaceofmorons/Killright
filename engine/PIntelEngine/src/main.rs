use std::io::{self, Read};

use pintel_engine::contracts::{AnalysisRequest, AnalysisResult};
use pintel_engine::recent_style::analyze_recent_style;

mod pintel_engine;

fn main() {
    let mut input = String::new();

    if io::stdin().read_to_string(&mut input).is_err() {
        print_error();
        return;
    }

    let request: AnalysisRequest = match serde_json::from_str(&input) {
        Ok(value) => value,
        Err(_) => {
            print_error();
            return;
        }
    };

    let result = analyze_recent_style(request);

    match serde_json::to_string(&result) {
        Ok(json) => println!("{}", json),
        Err(_) => print_error(),
    }
}

fn print_error() {
    let result = AnalysisResult {
        character_id: 0,
        recent_style: "Unknown".to_string(),
        analyzed_killmails: 0,
        kills: 0,
        losses: 0,
        solo_losses: 0,
    };

    println!("{}", serde_json::to_string(&result).unwrap());
}