use serde::Deserialize;

use super::engine_inputs::{GroupInputs, PilotInputs};

#[derive(Debug, Deserialize)]
pub struct PilotAnalysisRequest {
    pub character_id: i64,

    #[serde(default)]
    pub scanned_character_ids: Option<Vec<i64>>,

    #[serde(default)]
    pub pilot: Option<PilotInputs>,

    #[serde(default)]
    pub group_inputs: Option<GroupInputs>,
}
