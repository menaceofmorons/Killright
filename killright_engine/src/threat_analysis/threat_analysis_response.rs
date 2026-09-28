use serde::Serialize;

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ThreatAnalysisResponse {
    pub score: i32,
}
