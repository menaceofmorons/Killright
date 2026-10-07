use serde::Deserialize;

use super::engine_inputs::PilotInputs;

#[derive(Debug, Deserialize)]
pub struct PilotsAnalysisRequest {
    pub character_ids: Vec<i64>,

    #[serde(default)]
    pub pilots: Vec<PilotInputs>,
}
