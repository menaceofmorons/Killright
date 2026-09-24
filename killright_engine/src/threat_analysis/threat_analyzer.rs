use chrono::{DateTime, Utc};

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
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
) -> ThreatAnalysisResponse {
    let confidence = calculate_confidence(&configuration.confidence, statistics, recent_killmails);

    if is_threat_none(statistics, recent_killmails) {
        return ThreatAnalysisResponse { score: 0, confidence };
    }

    let historical_capability =
        calculate_historical_capability_score(&configuration.historical_capability, statistics);

    let survivability = calculate_survivability_score(&configuration.survivability, statistics);

    let loss_quality = calculate_loss_quality_score(&configuration.loss_quality, statistics);

    let recent_activity = calculate_recent_activity_modifier(
        &configuration.recent_activity,
        recent_killmails,
        coverage_start_utc,
        now,
        recent_window_days,
    );

    let security = calculate_security_modifier(&configuration.security_status, identity);

    let raw_score = historical_capability as f64
        + survivability as f64
        + loss_quality as f64
        + recent_activity
        + security as f64;

    let score = raw_score.round().clamp(0.0, 100.0) as i32;

    ThreatAnalysisResponse { score, confidence }
}

pub(crate) fn is_threat_none(
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
) -> bool {
    match statistics {
        None => recent_killmails.is_empty(),
        Some(statistics) => statistics.no_history_marker || statistics.ships_destroyed == 0,
    }
}

pub(crate) fn calculate_historical_capability_score(
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

pub(crate) fn calculate_survivability_score(
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

pub(crate) fn calculate_loss_quality_score(
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

pub(crate) struct RecentActivityDiagnostics {
    pub coverage_start_present: bool,
    pub observed_days: f64,
    pub counted_kills: i64,
    pub daily_rate: f64,
    pub score: f64,
}

fn calculate_recent_activity_modifier(
    configuration: &RecentActivityConfiguration,
    recent_killmails: &[RecentKillmailSnapshot],
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
) -> f64 {
    calculate_recent_activity_diagnostics(
        configuration,
        recent_killmails,
        coverage_start_utc,
        now,
        recent_window_days,
    )
    .score
}

pub(crate) fn calculate_recent_activity_diagnostics(
    configuration: &RecentActivityConfiguration,
    recent_killmails: &[RecentKillmailSnapshot],
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
) -> RecentActivityDiagnostics {
    let Some(coverage_start_utc) = coverage_start_utc else {
        return RecentActivityDiagnostics {
            coverage_start_present: false,
            observed_days: 0.0,
            counted_kills: 0,
            daily_rate: 0.0,
            score: 0.0,
        };
    };

    let observed_seconds = (now - coverage_start_utc).num_seconds().max(0);
    let window_seconds = recent_window_days.max(0) * 86_400;
    let observed_days = observed_seconds.min(window_seconds) as f64 / 86_400.0;

    if observed_days <= 0.0 {
        return RecentActivityDiagnostics {
            coverage_start_present: true,
            observed_days: 0.0,
            counted_kills: 0,
            daily_rate: 0.0,
            score: 0.0,
        };
    }

    let counted_kills = recent_killmails
        .iter()
        .filter(|killmail| {
            !killmail.is_loss
                && DateTime::parse_from_rfc3339(&killmail.kill_time_utc)
                    .map(|value| value.with_timezone(&Utc) >= coverage_start_utc)
                    .unwrap_or(false)
        })
        .count();

    let daily_rate = counted_kills as f64 / observed_days;
    let score = interpolate_recent_activity_score(&configuration.points, daily_rate);

    RecentActivityDiagnostics {
        coverage_start_present: true,
        observed_days,
        counted_kills: counted_kills as i64,
        daily_rate,
        score,
    }
}

fn interpolate_recent_activity_score(points: &[RecentActivityPoint], daily_rate: f64) -> f64 {
    let Some(first) = points.first() else {
        return 0.0;
    };

    if daily_rate <= first.daily_rate {
        return first.score;
    }

    let last = &points[points.len() - 1];

    if daily_rate >= last.daily_rate {
        return last.score;
    }

    for window in points.windows(2) {
        let (lower, upper) = (&window[0], &window[1]);

        if daily_rate >= lower.daily_rate && daily_rate <= upper.daily_rate {
            let span = upper.daily_rate - lower.daily_rate;
            let progress = if span > 0.0 {
                (daily_rate - lower.daily_rate) / span
            } else {
                0.0
            };

            return lower.score + (upper.score - lower.score) * progress;
        }
    }

    last.score
}

pub(crate) fn calculate_security_modifier(
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

pub(crate) fn calculate_confidence(
    configuration: &ConfidenceConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
) -> i32 {
    let Some(statistics) = statistics else {
        return 0;
    };

    if statistics.no_history_marker || statistics.ships_destroyed <= 0 {
        return 100;
    }

    let recent_bonus = if recent_killmails.is_empty() {
        0.0
    } else {
        configuration.recent_activity_bonus
    };

    let value = configuration.kill_weight * (statistics.ships_destroyed as f64).min(configuration.kill_cap)
        + recent_bonus;

    value.round().clamp(0.0, 100.0) as i32
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
    use chrono::TimeZone;

    fn configuration() -> ThreatConfiguration {
        let text = include_str!("../config/threat-analysis.json").trim_start_matches('\u{feff}');

        serde_json::from_str(text).expect("test threat configuration must parse")
    }

    fn now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 9, 22, 0, 0, 0).unwrap()
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

    fn killmail_at(is_loss: bool, kill_time_utc: &str) -> RecentKillmailSnapshot {
        RecentKillmailSnapshot {
            killmail_id: 1,
            killmail_hash: None,
            character_id: 1,
            kill_time_utc: kill_time_utc.to_string(),
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

    fn killmail(is_loss: bool) -> RecentKillmailSnapshot {
        killmail_at(is_loss, "2026-09-20T00:00:00Z")
    }

    #[test]
    fn no_statistics_and_no_killmails_returns_none_score_and_zero_confidence() {
        let result = analyze_intrinsic_threat(&configuration(), None, &[], None, None, now(), 14);

        assert_eq!(result.score, 0);
        assert_eq!(result.confidence, 0);
    }

    #[test]
    fn no_history_marker_returns_none_score_with_maximum_confidence() {
        let stats = statistics(0, 0, true);
        let killmails = vec![killmail(false)];

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &killmails, None, None, now(), 14);

        assert_eq!(result.score, 0);
        assert_eq!(result.confidence, 100);
    }

    #[test]
    fn zero_kills_in_statistics_returns_none_score_with_maximum_confidence_regardless_of_losses() {
        let stats = statistics(0, 5, false);

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &[], None, None, now(), 14);

        assert_eq!(result.score, 0);
        assert_eq!(result.confidence, 100);
    }

    #[test]
    fn real_kill_history_returns_scored_result_with_formula_confidence() {
        let stats = statistics(500, 50, false);

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &[], None, None, now(), 14);

        assert!(result.score > 0);
        assert_eq!(result.confidence, 90);
    }

    #[test]
    fn confidence_uses_kills_only_ignoring_losses_and_caps_at_hundred() {
        let stats = statistics(150, 999, false);

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &[], None, None, now(), 14);

        assert_eq!(result.confidence, 90);
    }

    #[test]
    fn confidence_adds_recent_activity_bonus_when_a_recent_killmail_exists() {
        let stats = statistics(10, 0, false);
        let killmails = vec![killmail(false)];

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &killmails, None, None, now(), 14);

        assert_eq!(result.confidence, 19);
    }

    #[test]
    fn statistics_missing_but_recent_killmails_present_is_scored_from_recent_activity_alone() {
        let coverage_start = now() - chrono::Duration::days(3);
        let killmails = vec![
            killmail_at(false, "2026-09-20T00:00:00Z"),
            killmail_at(false, "2026-09-20T12:00:00Z"),
            killmail_at(false, "2026-09-21T00:00:00Z"),
        ];

        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
        );

        assert_eq!(result.score, 2);
        assert_eq!(result.confidence, 0);
    }

    #[test]
    fn recent_activity_modifier_reaches_maximum_at_high_daily_rate() {
        let coverage_start = now() - chrono::Duration::days(1);
        let killmails: Vec<RecentKillmailSnapshot> = (0..20)
            .map(|_| killmail_at(false, "2026-09-21T12:00:00Z"))
            .collect();

        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
        );

        assert_eq!(result.score, 10);
    }

    #[test]
    fn recent_activity_modifier_excludes_losses_and_kills_before_coverage_start() {
        let coverage_start = now() - chrono::Duration::days(2);
        let killmails = vec![
            killmail_at(true, "2026-09-21T00:00:00Z"),
            killmail_at(false, "2026-09-19T00:00:00Z"),
            killmail_at(false, "2026-09-21T00:00:00Z"),
        ];

        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
        );

        assert_eq!(result.score, 1);
    }
}
