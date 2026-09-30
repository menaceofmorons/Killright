use crate::recent_style::kill_style_classifier::classify_kill_style;
use crate::recent_style::victim_style_classifier::classify_victim_style;
use crate::recent_style::{RecentKillmailInput, RecentStyleRequest, RecentStyleResult};
use crate::shared::pod_kill::is_pod_kill;
use crate::shared::recent_style_contract::*;
use crate::shared::style_configuration::StyleConfiguration;

pub fn analyze_recent_style(request: RecentStyleRequest, style_configuration: &StyleConfiguration) -> RecentStyleResult {
    let analyzed_killmails = request.killmails.len();

    let kills: Vec<RecentKillmailInput> = request
        .killmails
        .iter()
        .filter(|killmail| !killmail.is_loss && !is_pod_kill(killmail.ship_type_id))
        .cloned()
        .collect();

    let pod_kills = request
        .killmails
        .iter()
        .filter(|killmail| !killmail.is_loss && is_pod_kill(killmail.ship_type_id))
        .count();

    let losses: Vec<RecentKillmailInput> = request
        .killmails
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
        STYLE_INACTIVE.to_string()
    } else if is_recent_victim(kill_count, loss_count, solo_losses) {
        classify_victim_style(&losses)
    } else if kill_count > 0 {
        classify_kill_style(&kills, style_configuration)
    } else {
        STYLE_VICTIM.to_string()
    };

    let is_podder = is_recent_podder(&recent_style, kill_count, pod_kills, style_configuration);

    RecentStyleResult {
        character_id: request.character_id,
        recent_style,
        analyzed_killmails,
        kills: kill_count,
        losses: loss_count,
        solo_losses,
        is_podder,
    }
}

fn is_recent_podder(
    recent_style: &str,
    non_pod_kill_count: usize,
    pod_kill_count: usize,
    style_configuration: &StyleConfiguration,
) -> bool {
    if recent_style != STYLE_SOLO && recent_style != STYLE_GANG && recent_style != STYLE_BLOB {
        return false;
    }

    if non_pod_kill_count == 0 || (pod_kill_count as i64) < style_configuration.podder_minimum_kill_count {
        return false;
    }

    let share_percent = pod_kill_count as f64 / non_pod_kill_count as f64 * 100.0;

    share_percent >= style_configuration.podder_minimum_share_percent
}

fn is_recent_victim(kill_count: usize, loss_count: usize, solo_losses: usize) -> bool {
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

    fn style_configuration() -> StyleConfiguration {
        StyleConfiguration {
            blob_minimum_average_attackers: 5.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
        }
    }

    fn request(killmails: Vec<RecentKillmailInput>) -> RecentStyleRequest {
        RecentStyleRequest {
            character_id: 123,
            killmails,
        }
    }

    fn kill(
        killmail_id: i64,
        attacker_count: i32,
        is_solo: bool,
        ship_type_id: Option<i64>,
    ) -> RecentKillmailInput {
        RecentKillmailInput {
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
        ship_type_id: Option<i64>,
    ) -> RecentKillmailInput {
        RecentKillmailInput {
            killmail_id,
            is_loss: true,
            attacker_count,
            is_solo,
            ship_type_id,
        }
    }

    #[test]
    fn empty_request_returns_inactive_contract_value() {
        let result = analyze_recent_style(request(vec![]), &style_configuration());

        assert_eq!(result.recent_style, STYLE_INACTIVE);
        assert_eq!(result.analyzed_killmails, 0);
    }

    #[test]
    fn solo_kill_returns_solo_contract_value() {
        let result = analyze_recent_style(request(vec![kill(1, 1, true, Some(33468))]), &style_configuration());

        assert_eq!(result.recent_style, STYLE_SOLO);
        assert_eq!(result.kills, 1);
        assert_eq!(result.losses, 0);
    }

    #[test]
    fn victim_ratio_with_unknown_ship_returns_victim_contract_value() {
        let result = analyze_recent_style(
            request(vec![
                loss(1, 1, true, Some(999999)),
                loss(2, 1, true, Some(999999)),
                loss(3, 1, true, Some(999999)),
            ]),
            &style_configuration(),
        );

        assert_eq!(result.recent_style, STYLE_VICTIM);
        assert_eq!(result.losses, 3);
        assert_eq!(result.solo_losses, 3);
    }

    #[test]
    fn victim_ratio_requires_low_kill_loss_ratio() {
        let result = analyze_recent_style(
            request(vec![
                kill(1, 1, true, None),
                loss(2, 1, true, Some(999999)),
                loss(3, 1, true, Some(999999)),
            ]),
            &style_configuration(),
        );

        assert_ne!(result.recent_style, STYLE_VICTIM);
    }

    #[test]
    fn pod_kills_are_excluded_from_kill_count_and_style_classification() {
        let result = analyze_recent_style(
            request(vec![
                kill(1, 1, true, Some(670)),
                kill(2, 1, true, Some(33328)),
                kill(3, 1, true, Some(33468)),
            ]),
            &style_configuration(),
        );

        assert_eq!(result.kills, 1);
        assert_eq!(result.recent_style, STYLE_SOLO);
    }

    #[test]
    fn recent_podder_marker_set_when_pod_share_and_count_meet_minimums() {
        let kills = (0..4)
            .map(|id| kill(id, 4, false, Some(33468)))
            .chain((0..5).map(|id| kill(100 + id, 4, false, Some(670))))
            .collect();

        let result = analyze_recent_style(request(kills), &style_configuration());

        assert_eq!(result.recent_style, STYLE_GANG);
        assert!(result.is_podder);
    }

    #[test]
    fn recent_podder_marker_set_at_thirty_five_percent_pod_share() {
        let kills = (0..20)
            .map(|id| kill(id, 4, false, Some(33468)))
            .chain((0..7).map(|id| kill(100 + id, 4, false, Some(670))))
            .collect();

        let result = analyze_recent_style(request(kills), &style_configuration());

        assert_eq!(result.recent_style, STYLE_GANG);
        assert!(result.is_podder);
    }

    #[test]
    fn recent_podder_marker_not_set_at_thirty_four_percent_pod_share() {
        let kills = (0..100)
            .map(|id| kill(id, 4, false, Some(33468)))
            .chain((0..34).map(|id| kill(1000 + id, 4, false, Some(670))))
            .collect();

        let result = analyze_recent_style(request(kills), &style_configuration());

        assert_eq!(result.recent_style, STYLE_GANG);
        assert!(!result.is_podder);
    }

    #[test]
    fn recent_podder_marker_not_set_below_minimum_pod_kill_count() {
        let kills = (0..20)
            .map(|id| kill(id, 4, false, Some(33468)))
            .chain((0..4).map(|id| kill(100 + id, 4, false, Some(670))))
            .collect();

        let result = analyze_recent_style(request(kills), &style_configuration());

        assert_eq!(result.recent_style, STYLE_GANG);
        assert!(!result.is_podder);
    }

    #[test]
    fn recent_podder_marker_not_set_for_fleet_style() {
        let kills = (0..4)
            .map(|id| kill(id, 12, false, Some(33468)))
            .chain((0..5).map(|id| kill(100 + id, 12, false, Some(670))))
            .collect();

        let result = analyze_recent_style(request(kills), &style_configuration());

        assert_eq!(result.recent_style, STYLE_FLEET);
        assert!(!result.is_podder);
    }

    #[test]
    fn only_pod_kills_in_window_never_resolves_to_solo_gang_or_blob() {
        let result = analyze_recent_style(
            request(vec![kill(1, 1, true, Some(670)), kill(2, 1, true, Some(33328))]),
            &style_configuration(),
        );

        assert_ne!(result.recent_style, STYLE_SOLO);
        assert_ne!(result.recent_style, STYLE_GANG);
        assert_ne!(result.recent_style, STYLE_BLOB);
        assert!(!result.is_podder);
    }
}
