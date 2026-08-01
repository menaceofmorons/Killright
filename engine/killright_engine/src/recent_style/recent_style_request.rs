use crate::recent_style::recent_killmail_input::RecentKillmailInput;
use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct RecentStyleRequest {
    pub character_id: i64,
    pub killmails: Vec<RecentKillmailInput>,
}
