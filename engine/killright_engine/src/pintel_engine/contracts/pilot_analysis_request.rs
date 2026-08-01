use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct PilotAnalysisRequest {
    pub character_id: i64,
}