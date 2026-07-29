use crate::pintel_engine::contracts::KillmailInput;
use crate::pintel_engine::ship_library::classifier::classify_ship;

pub fn is_victim_by_ratio(
    recent_kills: usize,
    total_recent_losses: usize,
    solo_recent_losses: usize) -> bool {
    if total_recent_losses == 0 {
        return false;
    }

    let kills_to_losses_ratio = recent_kills as f64 / total_recent_losses as f64;
    let solo_losses_to_losses_ratio = solo_recent_losses as f64 / total_recent_losses as f64;

    kills_to_losses_ratio <= 0.2 && solo_losses_to_losses_ratio >= 0.7
}

pub fn classify_victim_subtype(losses: &[&KillmailInput]) -> String {
    for loss in losses {
        if let Some(classification) = classify_ship(loss.ship_type_id) {
            return classification;
        }
    }

    "Victim".to_string()
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
    fn victim_ratio_requires_losses() {
        assert!(!is_victim_by_ratio(0, 0, 0));
    }

    #[test]
    fn victim_ratio_accepts_low_kills_and_high_solo_losses() {
        assert!(is_victim_by_ratio(0, 5, 5));
    }

    #[test]
    fn unknown_loss_returns_victim() {
        let killmail = loss(Some(999999));
        assert_eq!(classify_victim_subtype(&[&killmail]), "Victim");
    }

    #[test]
    fn null_ship_loss_returns_victim() {
        let killmail = loss(None);
        assert_eq!(classify_victim_subtype(&[&killmail]), "Victim");
    }
}