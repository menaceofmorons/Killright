use std::collections::HashMap;

use crate::pintel_engine::contracts::killmail_input::KillmailInput;
use crate::pintel_engine::ship_library::ship_classifier::classify_ship;

pub fn classify_victim_style(losses: &[KillmailInput]) -> String {
    classify_victim_style_with_lookup(losses, classify_ship)
}

fn classify_victim_style_with_lookup<F>(
    losses: &[KillmailInput],
    lookup: F) -> String
where
    F: Fn(Option<i64>) -> Option<String>,
{
    if losses.is_empty() {
        return "Victim".to_string();
    }

    let mut counts: HashMap<String, usize> = HashMap::new();

    for loss in losses {
        if let Some(classification) = lookup(loss.ship_type_id) {
            *counts.entry(classification).or_insert(0) += 1;
        }
    }

    counts
        .into_iter()
        .max_by_key(|(_, count)| *count)
        .map(|(classification, _)| classification)
        .unwrap_or_else(|| "Victim".to_string())
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
        let result = classify_victim_style_with_lookup(
            &[loss(None)],
            |_| None);

        assert_eq!(result, "Victim");
    }

    #[test]
    fn unknown_loss_returns_victim() {
        let result = classify_victim_style_with_lookup(
            &[loss(Some(999999))],
            |_| None);

        assert_eq!(result, "Victim");
    }

    #[test]
    fn pi_loss_returns_pi() {
        let result = classify_victim_style_with_lookup(
            &[
                loss(Some(655)),
                loss(Some(655)),
                loss(Some(655)),
            ],
            |ship_type_id| match ship_type_id {
                Some(655) => Some("PI".to_string()),
                _ => None,
            });

        assert_eq!(result, "PI");
    }

    #[test]
    fn explorer_loss_returns_explorer() {
        let result = classify_victim_style_with_lookup(
            &[
                loss(Some(605)),
                loss(Some(605)),
                loss(Some(605)),
            ],
            |ship_type_id| match ship_type_id {
                Some(605) => Some("Explorer".to_string()),
                _ => None,
            });

        assert_eq!(result, "Explorer");
    }

    #[test]
    fn most_common_victim_subtype_wins() {
        let result = classify_victim_style_with_lookup(
            &[
                loss(Some(655)),
                loss(Some(605)),
                loss(Some(605)),
            ],
            |ship_type_id| match ship_type_id {
                Some(655) => Some("PI".to_string()),
                Some(605) => Some("Explorer".to_string()),
                _ => None,
            });

        assert_eq!(result, "Explorer");
    }
}