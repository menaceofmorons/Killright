use std::collections::HashMap;

use crate::pintel_engine::contracts::killmail_input::KillmailInput;
use crate::pintel_engine::ship_library::ship_classifier::classify_ship;

pub fn classify_victim_style(losses: &[KillmailInput]) -> String {
    if losses.is_empty() {
        return "Vict".to_string();
    }

    let mut counts: HashMap<String, usize> = HashMap::new();

    for loss in losses {
        if let Some(classification) = classify_ship(loss.ship_type_id) {
            *counts.entry(classification).or_insert(0) += 1;
        }
    }

    counts
        .into_iter()
        .max_by_key(|(_, count)| *count)
        .map(|(classification, _)| classification)
        .unwrap_or_else(|| "Vict".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn loss(ship_type_id: Option<i64>) -> KillmailInput {
        KillmailInput {
            killmail_id: 1,
            is_loss: true,
            attacker_count: 1,
            is_solo: true,
            ship_type_id,
        }
    }

    #[test]
    fn null_ship_loss_returns_victim() {
        let result = classify_victim_style(&[
            loss(None),
        ]);

        assert_eq!(result, "Vict");
    }

    #[test]
    fn unknown_loss_returns_victim() {
        let result = classify_victim_style(&[
            loss(Some(999999)),
        ]);

        assert_eq!(result, "Vict");
    }
}