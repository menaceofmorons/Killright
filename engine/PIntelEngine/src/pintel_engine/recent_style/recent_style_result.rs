use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct RecentStyleResult {
    pub character_id: i64,
    pub recent_style: String,
    pub analyzed_killmails: usize,
    pub kills: usize,
    pub losses: usize,
    pub solo_losses: usize,
}