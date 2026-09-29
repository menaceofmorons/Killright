use std::collections::BTreeMap;

use serde::Serialize;

use crate::contracts::group_detection_response::GroupDetectionResponse;
use crate::threat_analysis::ThreatAnalysisResponse;

#[derive(Debug, Serialize)]
pub struct PilotAnalysisResponse {
    pub character_id: i64,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub recent_style: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub is_recent_podder: Option<bool>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub threat: Option<ThreatAnalysisResponse>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub group_detection: Option<GroupDetectionResponse>,

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

    fn response() -> PilotAnalysisResponse {
        PilotAnalysisResponse {
            character_id: 1,
            recent_style: None,
            is_recent_podder: None,
            threat: None,
            group_detection: None,
            failure: None,
            timings_ms: None,
            timing_counts: None,
        }
    }

    #[test]
    fn serialises_without_timing_fields_when_unset() {
        let json = serde_json::to_string(&response()).unwrap();

        assert!(!json.contains("timings_ms"));
        assert!(!json.contains("timing_counts"));
    }

    #[test]
    fn serialises_timing_fields_when_set() {
        let mut value = response();
        value.timings_ms = Some(BTreeMap::from([("pilot_total".to_string(), 1.5)]));
        value.timing_counts = Some(BTreeMap::from([("killmail_rows".to_string(), 4)]));

        let json = serde_json::to_string(&value).unwrap();

        assert!(json.contains("\"timings_ms\":{\"pilot_total\":1.5}"));
        assert!(json.contains("\"timing_counts\":{\"killmail_rows\":4}"));
    }
}
