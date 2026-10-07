use chrono::{DateTime, Utc};

use crate::contracts::PilotIdentitySnapshot;
use crate::contracts::RecentKillmailSnapshot;
use crate::contracts::ZKillStatisticsSnapshot;
use crate::shared::pod_kill::is_pod_kill;
use crate::shared::recent_style_contract::{
    STYLE_BLOB, STYLE_FLEET, STYLE_GANG, STYLE_INACTIVE, STYLE_SOLO, STYLE_UNKNOWN, STYLE_VICTIM,
};
use crate::threat_analysis::threat_analysis_response::ThreatAnalysisResponse;
use crate::threat_analysis::threat_configuration_models::*;

const GENERAL_STYLE_LABEL_VICTIM: &str = "Vict";
const GENERAL_STYLE_LABEL_INACTIVE: &str = "Inactive";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum GateRule {
    NoData,
    NonCombatNone,
    Capped,
    Uncapped,
}

impl GateRule {
    pub(crate) fn as_str(self) -> &'static str {
        match self {
            GateRule::NoData => "no_data",
            GateRule::NonCombatNone => "non_combat_none",
            GateRule::Capped => "capped",
            GateRule::Uncapped => "uncapped",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct ThreatGate {
    pub rule: GateRule,
    pub cap: Option<i32>,
}

pub fn analyze_intrinsic_threat(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    identity: Option<&PilotIdentitySnapshot>,
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
    recent_style: &str,
) -> ThreatAnalysisResponse {
    let gate = evaluate_threat_gate(&configuration.gating, statistics, recent_killmails, recent_style);

    if matches!(gate.rule, GateRule::NoData | GateRule::NonCombatNone) {
        return ThreatAnalysisResponse { score: 0 };
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

    let (score, _) = finalize_threat_score(&configuration.gating, &gate, raw_score, recent_style);

    ThreatAnalysisResponse { score }
}

pub(crate) fn is_threat_none(
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
) -> bool {
    match statistics {
        None => recent_killmails.is_empty(),
        Some(statistics) => statistics.no_history_marker,
    }
}

fn is_combat_recent_style(recent_style: &str) -> bool {
    matches!(
        recent_style,
        STYLE_SOLO | STYLE_GANG | STYLE_BLOB | STYLE_FLEET
    )
}

fn is_non_combat_recent_style(recent_style: &str) -> bool {
    matches!(recent_style, STYLE_VICTIM | STYLE_INACTIVE | STYLE_UNKNOWN)
}

fn is_non_combat_general_style(general_style: &str) -> bool {
    let label = general_style.trim();

    label.eq_ignore_ascii_case(GENERAL_STYLE_LABEL_VICTIM)
        || label.eq_ignore_ascii_case(GENERAL_STYLE_LABEL_INACTIVE)
}

fn ratio_cap(configuration: &GatingConfiguration, statistics: &ZKillStatisticsSnapshot) -> Option<i32> {
    let ship_losses = statistics.ship_losses();

    if ship_losses <= 0 {
        return None;
    }

    let ratio = statistics.ships_destroyed as f64 / ship_losses as f64;

    if ratio <= configuration.medium_ratio_maximum {
        Some(configuration.medium_cap)
    } else if ratio < configuration.high_ratio_below {
        Some(configuration.high_cap)
    } else {
        None
    }
}

pub(crate) fn evaluate_threat_gate(
    configuration: &GatingConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    recent_style: &str,
) -> ThreatGate {
    if is_threat_none(statistics, recent_killmails) {
        return ThreatGate {
            rule: GateRule::NoData,
            cap: None,
        };
    }

    let general_non_combat = statistics
        .map(|value| is_non_combat_general_style(&value.general_style))
        .unwrap_or(false);

    if general_non_combat && is_non_combat_recent_style(recent_style) {
        return ThreatGate {
            rule: GateRule::NonCombatNone,
            cap: None,
        };
    }

    let style_cap = if general_non_combat {
        Some(configuration.medium_cap)
    } else {
        None
    };
    let cap = [style_cap, statistics.and_then(|value| ratio_cap(configuration, value))]
        .into_iter()
        .flatten()
        .min();

    ThreatGate {
        rule: if cap.is_some() {
            GateRule::Capped
        } else {
            GateRule::Uncapped
        },
        cap,
    }
}

pub(crate) fn finalize_threat_score(
    configuration: &GatingConfiguration,
    gate: &ThreatGate,
    raw_score: f64,
    recent_style: &str,
) -> (i32, bool) {
    let mut score = raw_score.round().clamp(0.0, 100.0) as i32;

    if let Some(cap) = gate.cap {
        score = score.min(cap);
    }

    let floor_applied = is_combat_recent_style(recent_style) && score < configuration.floor;

    if floor_applied {
        score = configuration.floor;
    }

    (score, floor_applied)
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
    let losses = statistics.ship_losses();

    if kills <= 0 {
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

    if statistics.ships_destroyed <= 0 {
        return 0;
    }

    let ship_losses = statistics.ship_losses();

    if ship_losses <= 0 {
        return configuration.no_losses_with_kills_score;
    }

    let solo_loss_ratio = (statistics.solo_losses as f64 / ship_losses as f64).min(1.0);

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
                && !is_pod_kill(killmail.ship_type_id)
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
        .filter(|band| security_status < band.below_security_status)
        .map(|band| band.score)
        .max()
        .unwrap_or(0)
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
    use crate::contracts::RecentKillmailSnapshot;
    use chrono::TimeZone;

    fn configuration() -> ThreatConfiguration {
        serde_json::from_str(crate::threat_analysis::threat_test_fixtures::THREAT_CONFIGURATION_JSON)
            .expect("test threat configuration must parse")
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
            pod_losses: 0,
        }
    }

    fn statistics_with_style(
        ships_destroyed: i32,
        ships_lost: i32,
        general_style: &str,
    ) -> ZKillStatisticsSnapshot {
        let mut value = statistics(ships_destroyed, ships_lost, false);
        value.general_style = general_style.to_string();
        value
    }

    fn identity(security_status: Option<f64>) -> PilotIdentitySnapshot {
        PilotIdentitySnapshot {
            input_name: "Lukas Naarii".to_string(),
            character_id: Some(1),
            character_name: Some("Lukas Naarii".to_string()),
            verify_status: "Verified".to_string(),
            security_status,
            corporation_id: None,
            corporation_name: None,
            corporation_ticker: None,
            alliance_id: None,
            alliance_name: None,
            alliance_ticker: None,
            cached_at_utc: "2026-09-22T00:00:00Z".to_string(),
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

    fn busy_recent_killmails() -> Vec<RecentKillmailSnapshot> {
        (0..20)
            .map(|_| killmail_at(false, "2026-09-21T12:00:00Z"))
            .collect()
    }

    fn score(
        statistics: Option<&ZKillStatisticsSnapshot>,
        recent_killmails: &[RecentKillmailSnapshot],
        recent_style: &str,
    ) -> i32 {
        analyze_intrinsic_threat(
            &configuration(),
            statistics,
            recent_killmails,
            None,
            Some(now() - chrono::Duration::days(1)),
            now(),
            14,
            recent_style,
        )
        .score
    }

    #[test]
    fn no_statistics_and_no_killmails_returns_none_score() {
        let result = analyze_intrinsic_threat(&configuration(), None, &[], None, None, now(), 14, "Inactive");

        assert_eq!(result.score, 0);
    }

    #[test]
    fn no_history_marker_returns_none_score_even_with_combat_recent_style() {
        let stats = statistics(0, 0, true);
        let killmails = vec![killmail(false)];

        let result = analyze_intrinsic_threat(&configuration(), Some(&stats), &killmails, None, None, now(), 14, "Solo");

        assert_eq!(result.score, 0);
    }

    #[test]
    fn zero_statistics_kills_with_losses_scores_nothing_from_statistics_components() {
        let configuration = configuration();
        let stats = statistics(0, 5, false);

        assert_eq!(calculate_survivability_score(&configuration.survivability, Some(&stats)), 0);
        assert_eq!(calculate_loss_quality_score(&configuration.loss_quality, Some(&stats)), 0);
        assert_eq!(score(Some(&stats), &[], "Inactive"), 0);
    }

    #[test]
    fn real_kill_history_returns_scored_result() {
        let stats = statistics(500, 50, false);

        assert!(score(Some(&stats), &[], "Solo") > 0);
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
            "Gang",
        );

        assert_eq!(result.score, 2);
    }

    #[test]
    fn recent_activity_modifier_reaches_maximum_at_high_daily_rate() {
        let coverage_start = now() - chrono::Duration::days(1);
        let killmails = busy_recent_killmails();

        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
            "Gang",
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
            "Solo",
        );

        assert_eq!(result.score, 1);
    }

    #[test]
    fn recent_activity_modifier_excludes_pod_kills() {
        let coverage_start = now() - chrono::Duration::days(1);

        fn pod_killmail_at(kill_time_utc: &str) -> RecentKillmailSnapshot {
            RecentKillmailSnapshot {
                killmail_id: 1,
                killmail_hash: None,
                character_id: 1,
                kill_time_utc: kill_time_utc.to_string(),
                is_loss: false,
                attacker_count: 1,
                is_solo: true,
                ship_type_id: Some(670),
                system_id: None,
                location_id: None,
                is_npc: false,
                cached_at_utc: "2026-09-22T00:00:00Z".to_string(),
            }
        }

        let killmails: Vec<RecentKillmailSnapshot> = (0..20)
            .map(|_| pod_killmail_at("2026-09-21T12:00:00Z"))
            .collect();

        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
            "Inactive",
        );

        assert_eq!(result.score, 0);
    }

    #[test]
    fn survivability_uses_ship_losses_excluding_pod_losses() {
        let configuration = configuration();
        let mut with_pods = statistics(100, 60, false);
        with_pods.pod_losses = 50;
        let without_pods = statistics(100, 10, false);

        assert_eq!(
            calculate_survivability_score(&configuration.survivability, Some(&with_pods)),
            calculate_survivability_score(&configuration.survivability, Some(&without_pods))
        );
    }

    #[test]
    fn only_pod_losses_count_as_no_losses_for_survivability_and_loss_quality() {
        let configuration = configuration();
        let mut with_pod_losses = statistics(100, 20, false);
        with_pod_losses.pod_losses = 20;
        let no_losses = statistics(100, 0, false);

        assert_eq!(
            calculate_survivability_score(&configuration.survivability, Some(&with_pod_losses)),
            calculate_survivability_score(&configuration.survivability, Some(&no_losses))
        );
        assert_eq!(
            calculate_loss_quality_score(&configuration.loss_quality, Some(&with_pod_losses)),
            calculate_loss_quality_score(&configuration.loss_quality, Some(&no_losses))
        );
    }

    #[test]
    fn loss_quality_solo_loss_ratio_never_exceeds_one() {
        let configuration = configuration();
        let mut inflated = statistics(100, 20, false);
        inflated.pod_losses = 10;
        inflated.solo_losses = 20;
        let mut capped = statistics(100, 10, false);
        capped.solo_losses = 10;

        assert_eq!(
            calculate_loss_quality_score(&configuration.loss_quality, Some(&inflated)),
            calculate_loss_quality_score(&configuration.loss_quality, Some(&capped))
        );
    }

    #[test]
    fn general_victim_with_recent_inactive_victim_or_unknown_is_none() {
        let stats = statistics_with_style(1, 42, "Vict");

        for recent in ["Inactive", "Victim", "Unknown"] {
            assert_eq!(score(Some(&stats), &busy_recent_killmails(), recent), 0, "{recent}");
        }
    }

    #[test]
    fn general_inactive_with_recent_inactive_is_none() {
        let stats = statistics_with_style(0, 0, "Inactive");

        assert_eq!(score(Some(&stats), &busy_recent_killmails(), "Inactive"), 0);
    }

    #[test]
    fn general_victim_with_solo_recent_style_is_scored_capped_at_medium_and_never_none() {
        let stats = statistics_with_style(500, 10, "Vict");

        let value = score(Some(&stats), &busy_recent_killmails(), "Solo");

        assert!(value >= 1);
        assert!(value <= 40);
    }

    #[test]
    fn general_victim_style_cap_is_applied_to_a_high_raw_score() {
        let capped = statistics_with_style(5000, 1, "Vict");
        let uncapped = statistics_with_style(5000, 1, "Solo");

        assert_eq!(score(Some(&capped), &busy_recent_killmails(), "Solo"), 40);
        assert!(score(Some(&uncapped), &busy_recent_killmails(), "Solo") > 40);
    }

    #[test]
    fn general_inactive_zero_statistics_with_gang_recent_style_scores_from_recent_activity_and_security_only() {
        let stats = statistics_with_style(0, 0, "Inactive");
        let killmails = busy_recent_killmails();
        let pilot = identity(Some(-6.0));

        let result = analyze_intrinsic_threat(
            &configuration(),
            Some(&stats),
            &killmails,
            Some(&pilot),
            Some(now() - chrono::Duration::days(1)),
            now(),
            14,
            "Gang",
        );

        assert_eq!(result.score, 15);
    }

    #[test]
    fn floor_gives_one_for_a_combat_recent_style_that_computes_zero() {
        let stats = statistics_with_style(0, 42, "Vict");

        assert_eq!(score(Some(&stats), &[], "Solo"), 1);
        assert_eq!(score(Some(&stats), &[], "Fleet"), 1);
    }

    #[test]
    fn floor_is_not_applied_for_non_combat_recent_style_with_a_computed_zero() {
        let stats = statistics_with_style(0, 5, "Unknown");

        assert_eq!(score(Some(&stats), &[], "Victim"), 0);
    }

    #[test]
    fn ratio_caps_follow_the_configured_thresholds() {
        let configuration = configuration();
        let at_half = statistics_with_style(5000, 10000, "Solo");
        let above_half = statistics_with_style(5100, 10000, "Solo");
        let below_one = statistics_with_style(99, 100, "Solo");
        let at_one = statistics_with_style(5000, 5000, "Solo");
        let no_losses = statistics_with_style(5000, 0, "Solo");

        assert_eq!(ratio_cap(&configuration.gating, &at_half), Some(40));
        assert_eq!(ratio_cap(&configuration.gating, &above_half), Some(60));
        assert_eq!(ratio_cap(&configuration.gating, &below_one), Some(60));
        assert_eq!(ratio_cap(&configuration.gating, &at_one), None);
        assert_eq!(ratio_cap(&configuration.gating, &no_losses), None);
    }

    #[test]
    fn high_volume_pilot_is_capped_by_ratio_and_uncapped_ratio_is_unchanged() {
        let killmails = busy_recent_killmails();
        let half = statistics_with_style(5000, 10000, "Solo");
        let above_half = statistics_with_style(10000, 19000, "Solo");
        let even = statistics_with_style(10000, 10000, "Solo");

        assert_eq!(score(Some(&half), &killmails, "Solo"), 40);
        assert_eq!(score(Some(&above_half), &killmails, "Solo"), 60);
        assert!(score(Some(&even), &killmails, "Solo") > 60);
    }

    #[test]
    fn ratio_ignores_pod_losses() {
        let configuration = configuration();
        let mut stats = statistics_with_style(100, 400, "Solo");
        stats.pod_losses = 300;

        assert_eq!(ratio_cap(&configuration.gating, &stats), None);
    }

    #[test]
    fn lowest_applicable_cap_wins() {
        let configuration = configuration();
        let style_and_ratio = statistics_with_style(60, 100, "Vict");
        let ratio_only = statistics_with_style(10, 100, "Solo");

        let first = evaluate_threat_gate(&configuration.gating, Some(&style_and_ratio), &[], "Solo");
        let second = evaluate_threat_gate(&configuration.gating, Some(&ratio_only), &[], "Solo");

        assert_eq!(first.rule, GateRule::Capped);
        assert_eq!(first.cap, Some(40));
        assert_eq!(second.cap, Some(40));
    }

    #[test]
    fn low_volume_pilot_below_the_cap_is_unchanged() {
        let configuration = configuration();
        let stats = statistics_with_style(10, 100, "Solo");
        let uncapped_total = calculate_historical_capability_score(&configuration.historical_capability, Some(&stats))
            + calculate_survivability_score(&configuration.survivability, Some(&stats))
            + calculate_loss_quality_score(&configuration.loss_quality, Some(&stats));

        assert!(uncapped_total < 40);
        assert_eq!(score(Some(&stats), &[], "Solo"), uncapped_total);
    }

    #[test]
    fn statistics_missing_applies_no_general_style_or_ratio_gate() {
        let configuration = configuration();
        let killmails = vec![killmail(false)];
        let gate = evaluate_threat_gate(&configuration.gating, None, &killmails, "Solo");

        assert_eq!(gate.rule, GateRule::Uncapped);
        assert_eq!(gate.cap, None);
    }

    #[test]
    fn security_modifier_awards_points_only_below_each_threshold() {
        let configuration = configuration();
        let cases = [
            (Some(0.1), 0),
            (Some(0.0), 0),
            (Some(-0.5), 1),
            (Some(-2.0), 1),
            (Some(-2.5), 3),
            (Some(-5.0), 3),
            (Some(-5.5), 5),
            (Some(-10.0), 5),
            (None, 0),
        ];

        for (status, expected) in cases {
            let pilot = identity(status);

            assert_eq!(
                calculate_security_modifier(&configuration.security_status, Some(&pilot)),
                expected,
                "{status:?}"
            );
        }

        assert_eq!(calculate_security_modifier(&configuration.security_status, None), 0);
    }

    #[test]
    fn positive_security_status_never_returns_the_maximum() {
        let configuration = configuration();
        let pilot = identity(Some(5.0));

        assert_eq!(calculate_security_modifier(&configuration.security_status, Some(&pilot)), 0);
    }
}
