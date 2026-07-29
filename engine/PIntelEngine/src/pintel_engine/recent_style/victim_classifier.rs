use crate::pintel_engine::contracts::KillmailInput;

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
    if losses.iter().any(|loss| is_pi_ship(loss.ship_type_id)) {
        return "PI".to_string();
    }

    if losses.iter().any(|loss| is_mining_ship(loss.ship_type_id)) {
        return "Miner".to_string();
    }

    if losses.iter().any(|loss| is_explorer_ship(loss.ship_type_id)) {
        return "Explorer".to_string();
    }

    if losses.iter().any(|loss| is_hauler_ship(loss.ship_type_id)) {
        return "Hauler".to_string();
    }

    "Victim".to_string()
}

fn is_pi_ship(ship_type_id: Option<i64>) -> bool {
    matches!(ship_type_id, Some(655))
}

fn is_mining_ship(ship_type_id: Option<i64>) -> bool {
    matches!(
        ship_type_id,
        Some(32880)
            | Some(17476)
            | Some(17478)
            | Some(17480)
            | Some(22544)
            | Some(22546)
            | Some(22548)
            | Some(28606)
            | Some(42244)
            | Some(28352)
    )
}

fn is_explorer_ship(ship_type_id: Option<i64>) -> bool {
    matches!(
        ship_type_id,
        Some(605)
            | Some(607)
            | Some(586)
            | Some(593)
            | Some(33468)
            | Some(11192)
            | Some(11188)
            | Some(11172)
            | Some(11182)
    )
}

fn is_hauler_ship(ship_type_id: Option<i64>) -> bool {
    matches!(
        ship_type_id,
        Some(648)
            | Some(649)
            | Some(650)
            | Some(652)
            | Some(655)
            | Some(656)
            | Some(657)
            | Some(1944)
            | Some(19744)
            | Some(20183)
            | Some(20185)
            | Some(20187)
            | Some(20189)
            | Some(28844)
            | Some(28846)
            | Some(28848)
            | Some(28850)
    )
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
    fn pi_loss_returns_pi() {
        let killmail = loss(Some(655));
        assert_eq!(classify_victim_subtype(&[&killmail]), "PI");
    }

    #[test]
    fn unknown_loss_returns_victim() {
        let killmail = loss(Some(999999));
        assert_eq!(classify_victim_subtype(&[&killmail]), "Victim");
    }
}