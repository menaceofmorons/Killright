use crate::pintel_engine::contracts::analysis_request::AnalysisRequest;
use crate::pintel_engine::contracts::analysis_result::AnalysisResult;
use crate::pintel_engine::contracts::killmail_input::KillmailInput;
use crate::pintel_engine::recent_style::kill_style_classifier::classify_kill_style;
use crate::pintel_engine::recent_style::victim_style_classifier::classify_victim_style;
use crate::pintel_engine::shared::recent_style_contract::*;

pub fn analyze_recent_style(
    request: AnalysisRequest)
    -> AnalysisResult
{
    let analyzed_killmails = request.killmails.len();

    let kills: Vec<KillmailInput> = request.killmails
        .iter()
        .filter(|killmail| !killmail.is_loss)
        .cloned()
        .collect();

    let losses: Vec<KillmailInput> = request.killmails
        .iter()
        .filter(|killmail| killmail.is_loss)
        .cloned()
        .collect();

    let kill_count = kills.len();
    let loss_count = losses.len();

    let solo_losses = losses
        .iter()
        .filter(|loss| loss.is_solo || loss.attacker_count == 1)
        .count();

    let recent_style = if analyzed_killmails == 0 {
        STYLE_UNKNOWN.to_string()
    } else if is_recent_victim(kill_count, loss_count, solo_losses) {
        classify_victim_style(&losses)
    } else if kill_count > 0 {
        classify_kill_style(&kills)
    } else {
        STYLE_VICTIM.to_string()
    };

    AnalysisResult {
        character_id: request.character_id,
        recent_style,
        analyzed_killmails,
        kills: kill_count,
        losses: loss_count,
        solo_losses,
    }
}

fn is_recent_victim(
    kill_count: usize,
    loss_count: usize,
    solo_losses: usize)
    -> bool
{
    if loss_count == 0 {
        return false;
    }

    let kill_loss_ratio = kill_count as f64 / loss_count as f64;
    let solo_loss_ratio = solo_losses as f64 / loss_count as f64;

    kill_loss_ratio <= 0.2 && solo_loss_ratio >= 0.7
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

    fn kill(
        killmail_id: i64,
        attacker_count: i32,
        is_solo: bool,
        ship_type_id: Option<i64>)
        -> KillmailInput
    {
        KillmailInput {
            killmail_id,
            is_loss: false,
            attacker_count,
            is_solo,
            ship_type_id,
        }
    }

    fn loss(
        killmail_id: i64,
        attacker_count: i32,
        is_solo: bool,
        ship_type_id: Option<i64>)
        -> KillmailInput
    {
        KillmailInput {
            killmail_id,
            is_loss: true,
            attacker_count,
            is_solo,
            ship_type_id,
        }
    }

    #[test]
    fn empty_request_returns_unknown_contract_value() {
        let result = analyze_recent_style(request(vec![]));

        assert_eq!(result.recent_style, STYLE_UNKNOWN);
        assert_eq!(result.analyzed_killmails, 0);
    }

    #[test]
    fn solo_kill_returns_solo_contract_value() {
        let result = analyze_recent_style(request(vec![
            kill(1, 1, true, Some(33468)),
        ]));

        assert_eq!(result.recent_style, STYLE_SOLO);
        assert_eq!(result.kills, 1);
        assert_eq!(result.losses, 0);
    }

    #[test]
    fn victim_ratio_with_unknown_ship_returns_victim_contract_value() {
        let result = analyze_recent_style(request(vec![
            loss(1, 1, true, Some(999999)),
            loss(2, 1, true, Some(999999)),
            loss(3, 1, true, Some(999999)),
        ]));

        assert_eq!(result.recent_style, STYLE_VICTIM);
        assert_eq!(result.losses, 3);
        assert_eq!(result.solo_losses, 3);
    }

    #[test]
    fn victim_ratio_requires_low_kill_loss_ratio() {
        let result = analyze_recent_style(request(vec![
            kill(1, 1, true, None),
            loss(2, 1, true, Some(999999)),
            loss(3, 1, true, Some(999999)),
        ]));

        assert_ne!(result.recent_style, STYLE_VICTIM);
    }
}