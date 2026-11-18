use crate::recent_style::RecentKillmailInput;
use crate::shared::recent_style_contract::*;
use crate::shared::style_configuration::StyleConfiguration;

pub fn classify_kill_style(
    kills: &[RecentKillmailInput],
    style_configuration: &StyleConfiguration,
) -> String {
    if kills.is_empty() {
        return STYLE_UNKNOWN.to_string();
    }

    let solo_kills = kills
        .iter()
        .filter(|kill| kill.is_solo || kill.attacker_count == 1)
        .count();

    let solo_share_percent = solo_kills as f64 * 100.0 / kills.len() as f64;

    if solo_share_percent >= style_configuration.recent_solo_share_percent {
        return STYLE_SOLO.to_string();
    }

    let average_attackers = kills
        .iter()
        .map(|kill| kill.attacker_count as f64)
        .sum::<f64>()
        / kills.len() as f64;

    if average_attackers < style_configuration.blob_minimum_average_attackers {
        STYLE_GANG.to_string()
    } else if average_attackers < style_configuration.fleet_minimum_average_attackers {
        STYLE_BLOB.to_string()
    } else {
        STYLE_FLEET.to_string()
    }
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
            recent_solo_share_percent: 60.0,
        }
    }

    fn kill(attacker_count: i32, is_solo: bool) -> RecentKillmailInput {
        RecentKillmailInput {
            killmail_id: 1,
            is_loss: false,
            attacker_count,
            is_solo,
            ship_type_id: None,
        }
    }

    #[test]
    fn results_carry_lower_case_codes() {
        let configuration = style_configuration();

        assert_eq!(classify_kill_style(&[], &configuration), "unknown");
        assert_eq!(
            classify_kill_style(&[kill(1, true)], &configuration),
            "solo"
        );
        assert_eq!(
            classify_kill_style(&[kill(3, false)], &configuration),
            "gang"
        );
        assert_eq!(
            classify_kill_style(&[kill(6, false)], &configuration),
            "blob"
        );
        assert_eq!(
            classify_kill_style(&[kill(12, false)], &configuration),
            "fleet"
        );
    }

    #[test]
    fn solo_heavy_kills_return_solo() {
        let result = classify_kill_style(
            &[kill(1, true), kill(1, true), kill(3, false)],
            &style_configuration(),
        );

        assert_eq!(result, STYLE_SOLO);
    }

    #[test]
    fn small_average_gang_returns_gang() {
        let result = classify_kill_style(&[kill(2, false), kill(3, false)], &style_configuration());

        assert_eq!(result, STYLE_GANG);
    }

    #[test]
    fn medium_average_gang_returns_blob() {
        let result = classify_kill_style(&[kill(6, false), kill(8, false)], &style_configuration());

        assert_eq!(result, STYLE_BLOB);
    }

    #[test]
    fn large_average_gang_returns_fleet() {
        let result =
            classify_kill_style(&[kill(12, false), kill(20, false)], &style_configuration());

        assert_eq!(result, STYLE_FLEET);
    }

    #[test]
    fn solo_switches_at_the_configured_share() {
        let kills = [
            kill(1, true),
            kill(1, true),
            kill(1, true),
            kill(3, false),
            kill(3, false),
        ];
        let at_sixty = style_configuration();
        let above_sixty = StyleConfiguration {
            recent_solo_share_percent: 60.5,
            ..style_configuration()
        };
        let below_sixty = StyleConfiguration {
            recent_solo_share_percent: 40.0,
            ..style_configuration()
        };

        assert_eq!(classify_kill_style(&kills, &at_sixty), STYLE_SOLO);
        assert_eq!(classify_kill_style(&kills, &above_sixty), STYLE_GANG);
        assert_eq!(classify_kill_style(&kills[1..], &below_sixty), STYLE_SOLO);
        assert_eq!(classify_kill_style(&kills[2..], &above_sixty), STYLE_GANG);
    }

    #[test]
    fn respects_configured_boundaries_over_literals() {
        let configured = StyleConfiguration {
            blob_minimum_average_attackers: 3.0,
            fleet_minimum_average_attackers: 6.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
            recent_solo_share_percent: 60.0,
        };

        let result = classify_kill_style(&[kill(4, false), kill(4, false)], &configured);

        assert_eq!(result, STYLE_BLOB);
    }
}
