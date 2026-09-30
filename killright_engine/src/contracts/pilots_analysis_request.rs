use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct PilotsAnalysisRequest {
    pub character_ids: Vec<i64>,
}
