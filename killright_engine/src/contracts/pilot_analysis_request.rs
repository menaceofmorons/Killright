use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct PilotAnalysisRequest {
    pub character_id: i64,

    #[serde(default)]
    pub scanned_character_ids: Option<Vec<i64>>,
}
