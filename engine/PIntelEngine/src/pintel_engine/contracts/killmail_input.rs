use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct KillmailInput {
    pub killmail_id: i64,
    pub is_loss: bool,
    pub attacker_count: i32,
    pub is_solo: bool,
    pub ship_type_id: Option<i64>,
}