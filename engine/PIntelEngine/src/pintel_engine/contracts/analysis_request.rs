use serde::Deserialize;

use super::KillmailInput;

#[derive(Debug, Deserialize)]
pub struct AnalysisRequest {
    pub character_id: i64,
    pub killmails: Vec<KillmailInput>,
}