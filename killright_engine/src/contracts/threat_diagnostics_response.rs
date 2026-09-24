use serde::Serialize;

use crate::threat_analysis::threat_diagnostics::ThreatDiagnostics;

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ThreatDiagnosticsEnvelope {
    pub character_id: i64,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub diagnostics: Option<ThreatDiagnosticsResponse>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub failure: Option<String>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ThreatDiagnosticsResponse {
    pub historical_capability: i32,
    pub survivability: i32,
    pub loss_quality: i32,
    pub recent_activity_modifier: f64,
    pub security_modifier: i32,
    pub score: i32,
    pub confidence: i32,
    pub coverage_start_present: bool,
    pub observed_days: f64,
    pub counted_kills: i64,
    pub daily_rate: f64,
}

impl From<ThreatDiagnostics> for ThreatDiagnosticsResponse {
    fn from(value: ThreatDiagnostics) -> Self {
        Self {
            historical_capability: value.historical_capability,
            survivability: value.survivability,
            loss_quality: value.loss_quality,
            recent_activity_modifier: value.recent_activity_modifier,
            security_modifier: value.security_modifier,
            score: value.score,
            confidence: value.confidence,
            coverage_start_present: value.coverage_start_present,
            observed_days: value.observed_days,
            counted_kills: value.counted_kills,
            daily_rate: value.daily_rate,
        }
    }
}
