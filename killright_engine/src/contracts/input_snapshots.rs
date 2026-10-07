use serde::Deserialize;

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct RecentKillmailSnapshot {
    pub killmail_id: i64,
    pub killmail_hash: Option<String>,
    pub character_id: i64,
    pub kill_time_utc: String,
    pub is_loss: bool,
    pub attacker_count: i32,
    pub is_solo: bool,
    pub ship_type_id: Option<i64>,
    pub system_id: Option<i64>,
    pub location_id: Option<i64>,
    pub is_npc: bool,
    pub cached_at_utc: String,
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct ZKillStatisticsSnapshot {
    pub character_id: i64,
    pub ships_destroyed: i32,
    pub solo_kills: i32,
    pub solo_ratio: f64,
    pub avg_gang_size: f64,
    pub ships_lost: i32,
    pub solo_losses: i32,
    pub general_style: String,
    pub checked_at_utc: String,
    pub no_history_marker: bool,
    pub pod_losses: i32,
}

impl ZKillStatisticsSnapshot {
    pub fn ship_losses(&self) -> i32 {
        (self.ships_lost - self.pod_losses).max(0)
    }
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct PilotIdentitySnapshot {
    pub input_name: String,
    pub character_id: Option<i64>,
    pub character_name: Option<String>,
    pub verify_status: String,
    pub security_status: Option<f64>,
    pub corporation_id: Option<i64>,
    pub corporation_name: Option<String>,
    pub corporation_ticker: Option<String>,
    pub alliance_id: Option<i64>,
    pub alliance_name: Option<String>,
    pub alliance_ticker: Option<String>,
    pub cached_at_utc: String,
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct KillmailAttackerEvidence {
    pub killmail_id: i64,
    pub character_id: i64,
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub kill_time_utc: String,
    pub unique_attacker_count: i64,
}
