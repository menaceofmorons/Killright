use crate::pintel_engine::contracts::killmail_input::KillmailInput;

pub fn classify_kill_style(kills: &[KillmailInput]) -> String {
    if kills.is_empty() {
        return "Unk".to_string();
    }

    let solo_kills = kills
        .iter()
        .filter(|kill| kill.is_solo || kill.attacker_count == 1)
        .count();

    let solo_ratio = solo_kills as f64 / kills.len() as f64;

    if solo_ratio >= 0.6 {
        return "Solo".to_string();
    }

    let average_attackers = kills
        .iter()
        .map(|kill| kill.attacker_count as f64)
        .sum::<f64>() / kills.len() as f64;

    if average_attackers < 5.0 {
        "Gang".to_string()
    } else if average_attackers < 11.0 {
        "Blob".to_string()
    } else {
        "Fleet".to_string()
    }
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
        let result = classify_kill_style(&[
            kill(1, true),
            kill(1, true),
            kill(3, false),
        ]);

        assert_eq!(result, "Solo");
    }

    #[test]
    fn small_average_gang_returns_gang() {
        let result = classify_kill_style(&[
            kill(2, false),
            kill(3, false),
        ]);

        assert_eq!(result, "Gang");
    }

    #[test]
    fn medium_average_gang_returns_blob() {
        let result = classify_kill_style(&[
            kill(6, false),
            kill(8, false),
        ]);

        assert_eq!(result, "Blob");
    }

    #[test]
    fn large_average_gang_returns_fleet() {
        let result = classify_kill_style(&[
            kill(12, false),
            kill(20, false),
        ]);

        assert_eq!(result, "Fleet");
    }
}