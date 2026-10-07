use serde::Deserialize;

use super::input_snapshots::{
    KillmailAttackerEvidence, PilotIdentitySnapshot, RecentKillmailSnapshot, ZKillStatisticsSnapshot,
};

#[derive(Debug, Clone, Deserialize)]
pub struct PilotInputs {
    pub character_id: i64,
    pub killmails: Vec<RecentKillmailSnapshot>,
    #[serde(default)]
    pub statistics: Option<ZKillStatisticsSnapshot>,
    #[serde(default)]
    pub identity: Option<PilotIdentitySnapshot>,
    #[serde(default)]
    pub coverage_start_utc: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct GroupInputs {
    pub direct_evidence: Vec<KillmailAttackerEvidence>,
    pub chain_evidence: Vec<KillmailAttackerEvidence>,
    pub identities: Vec<PilotIdentitySnapshot>,
    pub npc_corporation_ids: Vec<i64>,
}
