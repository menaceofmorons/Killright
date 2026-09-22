use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;
use crate::repositories::recent_killmail_repository::RecentKillmailSnapshot;
use crate::repositories::zkill_statistics_repository::ZKillStatisticsSnapshot;
use crate::threat_analysis::threat_analysis_response::ThreatAnalysisResponse;
use crate::threat_analysis::threat_configuration_models::*;

pub fn analyze_intrinsic_threat(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    identity: Option<&PilotIdentitySnapshot>,
) -> ThreatAnalysisResponse {
    if is_threat_none(statistics, recent_killmails) {
        return none_response();
    }

    let historical_capability =
        calculate_historical_capability_score(&configuration.historical_capability, statistics);

    let survivability = calculate_survivability_score(&configuration.survivability, statistics);

    let loss_quality = calculate_loss_quality_score(&configuration.loss_quality, statistics);

    let recent_activity =
        calculate_recent_activity_modifier(&configuration.recent_activity, recent_killmails);

    let security = calculate_security_modifier(&configuration.security_status, identity);

    let score = (historical_capability + survivability + loss_quality + recent_activity + security)
        .clamp(0, 100);

    if score == 0 {
        return none_response();
    }

    ThreatAnalysisResponse {
        score,
        band: map_score_to_band(&configuration.bands, score),
        confidence: calculate_confidence(&configuration.confidence, statistics, recent_killmails),
    }
}

fn is_threat_none(
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
) -> bool {
    match statistics {
        None => recent_killmails.is_empty(),
        Some(statistics) => statistics.no_history_marker || statistics.ships_destroyed == 0,
    }
}

fn none_response() -> ThreatAnalysisResponse {
    ThreatAnalysisResponse {
        score: 0,
        band: "None".to_string(),
        confidence: "Low".to_string(),
    }
}

fn calculate_historical_capability_score(
    configuration: &HistoricalCapabilityConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
) -> i32 {
    let Some(statistics) = statistics else {
        return 0;
    };

    let kill_volume_score = configuration
        .kill_volume_bands
        .iter()
        .find(|band| statistics.ships_destroyed <= band.maximum_kills)
        .map(|band| band.score)
        .unwrap_or(0);

    let solo_kill_score = configuration
        .solo_kill_bands
        .iter()
        .find(|band| statistics.solo_kills <= band.maximum_solo_kills)
        .map(|band| band.score)
        .unwrap_or(0);

    let solo_ratio_score = if statistics.solo_ratio >= configuration.solo_ratio.minimum_ratio {
        configuration.solo_ratio.score
    } else {
        0
    };

    let style_score = configuration
        .style_modifiers
        .iter()
        .find(|modifier| style_matches(&statistics.general_style, &modifier.style))
        .map(|modifier| modifier.score)
        .unwrap_or(0);

    (kill_volume_score + solo_kill_score + solo_ratio_score + style_score).clamp(0, 40)
}

fn calculate_survivability_score(
    configuration: &SurvivabilityConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
) -> i32 {
    let Some(statistics) = statistics else {
        return 0;
    };

    let kills = statistics.ships_destroyed;
    let losses = statistics.ships_lost;

    if kills <= 0 && losses <= 0 {
        return 0;
    }

    if losses <= 0 {
        return configuration
            .no_loss_bands
            .iter()
            .filter(|band| kills >= band.minimum_kills)
            .map(|band| band.score)
            .max()
            .unwrap_or(0);
    }

    let ratio = kills as f64 / losses as f64;

    configuration
        .ratios
        .iter()
        .find(|band| ratio <= band.maximum_ratio)
        .map(|band| band.score)
        .unwrap_or(0)
}

fn calculate_loss_quality_score(
    configuration: &LossQualityConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
) -> i32 {
    let Some(statistics) = statistics else {
        return 0;
    };

    if statistics.ships_lost <= 0 {
        return if statistics.ships_destroyed > 0 {
            configuration.no_losses_with_kills_score
        } else {
            0
        };
    }

    let solo_loss_ratio = statistics.solo_losses as f64 / statistics.ships_lost as f64;

    let style_bands = configuration
        .styles
        .iter()
        .find(|style| style_matches(&statistics.general_style, &style.style))
        .map(|style| style.bands.as_slice())
        .unwrap_or(configuration.default_style.bands.as_slice());

    style_bands
        .iter()
        .find(|band| solo_loss_ratio >= band.minimum_solo_loss_ratio)
        .map(|band| band.score)
        .unwrap_or(0)
}

fn calculate_recent_activity_modifier(
    configuration: &RecentActivityConfiguration,
    recent_killmails: &[RecentKillmailSnapshot],
) -> i32 {
    let recent_kills = recent_killmails
        .iter()
        .filter(|killmail| !killmail.is_loss)
        .count();

    configuration
        .bands
        .iter()
        .find(|band| recent_kills <= band.maximum_recent_kills)
        .map(|band| band.score)
        .unwrap_or(0)
}

fn calculate_security_modifier(
    configuration: &SecurityStatusConfiguration,
    identity: Option<&PilotIdentitySnapshot>,
) -> i32 {
    let Some(identity) = identity else {
        return 0;
    };

    let Some(security_status) = identity.security_status else {
        return 0;
    };

    configuration
        .bands
        .iter()
        .filter(|band| security_status >= band.minimum_security_status)
        .map(|band| band.score)
        .max()
        .unwrap_or(0)
}

fn calculate_confidence(
    configuration: &ConfidenceConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
) -> String {
    let Some(statistics) = statistics else {
        return "Low".to_string();
    };

    let historical_volume = statistics.ships_destroyed + statistics.ships_lost;

    if historical_volume >= configuration.high.minimum_historical_volume
        && (!configuration.high.requires_recent_activity || !recent_killmails.is_empty())
    {
        return "High".to_string();
    }

    if historical_volume >= configuration.medium.minimum_historical_volume {
        return "Medium".to_string();
    }

    "Low".to_string()
}

fn map_score_to_band(bands: &[ThreatBandConfiguration], score: i32) -> String {
    bands
        .iter()
        .find(|band| score >= band.minimum_score && score <= band.maximum_score)
        .map(|band| band.name.clone())
        .unwrap_or_else(|| "None".to_string())
}

fn style_matches(actual: &str, expected: &str) -> bool {
    actual
        .trim()
        .to_ascii_lowercase()
        .contains(&expected.trim().to_ascii_lowercase())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::repositories::recent_killmail_repository::RecentKillmailSnapshot;

    fn configuration() -> ThreatConfiguration {
        let text = include_str!("../config/threat-analysis.json").trim_start_matches('\u{feff}');

        serde_json::from_str(text).expect("test threat configuration must parse")
    }

    fn statistics(ships_destroyed: i32, ships_lost: i32, no_history_marker: bool) -> ZKillStatisticsSnapshot {
        ZKillStatisticsSnapshot {
            character_id: 1,
            ships_destroyed,
            solo_kills: 0,
            solo_ratio: 0.0,
            avg_gang_size: 0.0,
            ships_lost,
            solo_losses: 0,
            general_style: "Unknown".to_string(),
            checked_at_utc: "2026-09-22T00:00:00Z".to_string(),
            no_history_marker,
        }
    }

    fn killmail(is_loss: bool) -> RecentKillmailSnapshot {
        RecentKillmailSnapshot {
            killmail_id: 1,
            killmail_hash: None,
            character_id: 1,
            kill_time_utc: "2026-09-20T00:00:00Z".to_string(),
            is_loss,
            attacker_count: 1,
            is_solo: true,
            ship_type_id: None,
            system_id: None,
            location_id: None,
            is_npc: false,
            cached_at_utc: "2026-09-22T00:00:00Z".to_string(),
        }
    }

    #[test]
    fn no_statistics_and_no_killmails_returns_none_band() {
        let result = analyze_intrinsic_threat(&configuration(), None, &[], None);

        assert_eq!(result.band, "None");
        assert_eq!(result.score, 0);
        assert_eq!(result.confidence, "Low");
    }

    #[test]
    fn no_history_marker_returns_none_band_even_with_recent_killmails() {
        let stats = statistics(0, 0, true);
        let killmails = vec![killmail(false)];

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &killmails, None);

        assert_eq!(result.band, "None");
    }

    #[test]
    fn zero_kills_in_statistics_returns_none_band_regardless_of_losses() {
        let stats = statistics(0, 5, false);

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &[], None);

        assert_eq!(result.band, "None");
    }

    #[test]
    fn real_kill_history_returns_scored_band_not_none() {
        let stats = statistics(500, 50, false);

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &[], None);

        assert_ne!(result.band, "None");
        assert!(result.score > 0);
    }

    #[test]
    fn statistics_present_but_missing_with_recent_killmails_is_scored_not_none() {
        let killmails = vec![killmail(false), killmail(false), killmail(false)];

        let result = analyze_intrinsic_threat(&configuration(), None, &killmails, None);

        assert_ne!(result.band, "None");
        assert!(result.score > 0);
    }
}
