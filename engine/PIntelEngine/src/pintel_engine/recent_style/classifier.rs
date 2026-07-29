use crate::pintel_engine::contracts::{AnalysisRequest, AnalysisResult, KillmailInput};

use super::kill_style_classifier::classify_kill_style;
use super::victim_classifier::{classify_victim_subtype, is_victim_by_ratio};

pub fn analyze_recent_style(request: AnalysisRequest) -> AnalysisResult {
    let analyzed_killmails = request.killmails.len();

    if analyzed_killmails == 0 {
        return AnalysisResult {
            character_id: request.character_id,
            recent_style: "Unknown".to_string(),
            analyzed_killmails,
            kills: 0,
            losses: 0,
            solo_losses: 0,
        };
    }

    let losses: Vec<&KillmailInput> = request
        .killmails
        .iter()
        .filter(|killmail| killmail.is_loss)
        .collect();

    let kills: Vec<&KillmailInput> = request
        .killmails
        .iter()
        .filter(|killmail| !killmail.is_loss)
        .collect();

    let recent_kill_count = kills.len();
    let total_recent_loss_count = losses.len();
    let solo_recent_loss_count = losses
        .iter()
        .filter(|loss| loss.is_solo || loss.attacker_count == 1)
        .count();

    let recent_style = if is_victim_by_ratio(
        recent_kill_count,
        total_recent_loss_count,
        solo_recent_loss_count)
    {
        classify_victim_subtype(&losses)
    } else if recent_kill_count == 0 {
        "Unknown".to_string()
    } else {
        classify_kill_style(&kills)
    };

    AnalysisResult {
        character_id: request.character_id,
        recent_style,
        analyzed_killmails,
        kills: recent_kill_count,
        losses: total_recent_loss_count,
        solo_losses: solo_recent_loss_count,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request(killmails: Vec<KillmailInput>) -> AnalysisRequest {
        AnalysisRequest {
            character_id: 123,
            killmails,
        }
    }

    fn kill(id: i64, attacker_count: i32, is_solo: bool, ship_type_id: Option<i64>) -> KillmailInput {
        KillmailInput {
            killmail_id: id,
            is_loss: false,
            attacker_count,
            is_solo,
            ship_type_id,
        }
    }

    fn loss(id: i64, attacker_count: i32, is_solo: bool, ship_type_id: Option<i64>) -> KillmailInput {
        KillmailInput {
            killmail_id: id,
            is_loss: true,
            attacker_count,
            is_solo,
            ship_type_id,
        }
    }

    #[test]
    fn empty_request_returns_unknown() {
        let result = analyze_recent_style(request(vec![]));

        assert_eq!(result.recent_style, "Unknown");
        assert_eq!(result.analyzed_killmails, 0);
    }

    #[test]
    fn solo_kill_returns_solo() {
        let result = analyze_recent_style(request(vec![kill(1, 1, true, Some(33468))]));

        assert_eq!(result.recent_style, "Solo");
        assert_eq!(result.kills, 1);
        assert_eq!(result.losses, 0);
    }

    #[test]
    fn victim_ratio_with_pi_loss_returns_victim_or_pi() {
        let result = analyze_recent_style(request(vec![
            loss(1, 1, true, Some(655)),
            loss(2, 1, true, Some(655)),
            loss(3, 1, true, Some(655)),
            loss(4, 1, true, Some(655)),
            loss(5, 1, true, Some(655)),
        ]));

        assert!(result.recent_style == "PI"
            || result.recent_style == "Victim");

        assert_eq!(result.kills, 0);
        assert_eq!(result.losses, 5);
        assert_eq!(result.solo_losses, 5);
    }
}