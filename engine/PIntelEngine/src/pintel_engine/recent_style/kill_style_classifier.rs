use crate::pintel_engine::contracts::KillmailInput;

pub fn classify_kill_style(kills: &[&KillmailInput]) -> String {
    let solo_kills = kills
        .iter()
        .filter(|killmail| killmail.is_solo || killmail.attacker_count == 1)
        .count();

    let solo_ratio = solo_kills as f64 / kills.len() as f64;

    if solo_ratio >= 0.60 {
        return "Solo".to_string();
    }

    let total_attackers: i32 = kills
        .iter()
        .map(|killmail| killmail.attacker_count.max(1))
        .sum();

    let average_attackers = total_attackers as f64 / kills.len() as f64;

    if average_attackers < 5.0 {
        return "Gang".to_string();
    }

    if average_attackers < 11.0 {
        return "Blob".to_string();
    }

    "Fleet".to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn kill(attacker_count: i32, is_solo: bool) -> KillmailInput {
        KillmailInput {
            killmail_id: 1,
            is_loss: false,
            attacker_count,
            is_solo,
            ship_type_id: None,
        }
    }

    #[test]
    fn solo_heavy_kills_return_solo() {
        let killmail = kill(1, true);
        assert_eq!(classify_kill_style(&[&killmail]), "Solo");
    }

    #[test]
    fn small_average_gang_returns_gang() {
        let killmail = kill(4, false);
        assert_eq!(classify_kill_style(&[&killmail]), "Gang");
    }

    #[test]
    fn medium_average_gang_returns_blob() {
        let killmail = kill(7, false);
        assert_eq!(classify_kill_style(&[&killmail]), "Blob");
    }

    #[test]
    fn large_average_gang_returns_fleet() {
        let killmail = kill(12, false);
        assert_eq!(classify_kill_style(&[&killmail]), "Fleet");
    }
}