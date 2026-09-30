use std::collections::BTreeMap;

use serde::Serialize;

use crate::contracts::pilot_analysis_response::PilotAnalysisResponse;

#[derive(Debug, Serialize)]
pub struct PilotsAnalysisResponse {
    pub results: Vec<PilotAnalysisResponse>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub failure: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub timings_ms: Option<BTreeMap<String, f64>>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub timing_counts: Option<BTreeMap<String, i64>>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn whole_call_failure_serialises_empty_results_and_reason() {
        let response = PilotsAnalysisResponse {
            results: Vec::new(),
            failure: Some("missing_runtime".to_string()),
            timings_ms: None,
            timing_counts: None,
        };

        assert_eq!(
            serde_json::to_string(&response).unwrap(),
            "{\"results\":[],\"failure\":\"missing_runtime\"}"
        );
    }
}
