use serde::Deserialize;

use crate::pintel_engine::contracts::killmail_input::KillmailInput;

#[derive(Debug, Deserialize)]
pub struct AnalysisRequest {
    pub character_id: i64,
    pub killmails: Vec<KillmailInput>,
}