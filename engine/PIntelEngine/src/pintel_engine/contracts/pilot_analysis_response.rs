use serde::Serialize;

#[derive(Debug, Serialize)]
pub struct PilotAnalysisResponse {
    pub character_id: i64,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub recent_style: Option<String>,
}