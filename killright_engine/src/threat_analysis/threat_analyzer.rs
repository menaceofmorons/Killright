use chrono::{DateTime, Utc};

use crate::contracts::PilotIdentitySnapshot;
use crate::contracts::RecentKillmailSnapshot;
use crate::contracts::ZKillStatisticsSnapshot;
use crate::shared::pod_kill::is_pod_kill;
use crate::shared::recent_style_contract::{
    STYLE_BLOB, STYLE_FLEET, STYLE_GANG, STYLE_INACTIVE, STYLE_SOLO, STYLE_UNKNOWN, STYLE_VICTIM,
};
use crate::threat_analysis::threat_analysis_response::ThreatAnalysisResponse;
use crate::threat_analysis::threat_configuration_loader::THREAT_SCORE_MAXIMUM;
use crate::threat_analysis::threat_configuration_models::*;

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

#[allow(
    clippy::too_many_arguments,
    reason = "public engine API; signature is fixed by its callers"
)]
pub fn analyze_intrinsic_threat(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    has_cached_killmails: bool,
    identity: Option<&PilotIdentitySnapshot>,
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
    recent_style: &str,
) -> ThreatAnalysisResponse {
    let gate = evaluate_threat_gate(
        &configuration.gating,
        statistics,
        has_cached_killmails,
        recent_style,
    );

    if matches!(gate.rule, GateRule::NoData | GateRule::NonCombatNone) {
        return ThreatAnalysisResponse { score: 0 };
    }

    let components = calculate_component_scores(configuration, statistics, identity);

    let recent_activity = calculate_recent_activity_diagnostics(
        &configuration.recent_activity,
        configuration
            .component_weights
            .recent_activity
            .maximum_score,
        recent_killmails,
        coverage_start_utc,
        now,
        recent_window_days,
    )
    .score;

    let raw_score = components.historical_capability as f64
        + components.survivability as f64
        + components.loss_quality as f64
        + recent_activity
        + components.security as f64;

    let (score, _) = finalize_threat_score(&configuration.gating, &gate, raw_score, recent_style);

    ThreatAnalysisResponse { score }
}

pub(crate) struct ComponentScores {
    pub historical_capability: i32,
    pub survivability: i32,
    pub loss_quality: i32,
    pub security: i32,
}

fn clamp_to_component_maximum(score: i32, maximum: i32) -> i32 {
    score.clamp(0, maximum.max(0))
}

pub(crate) fn calculate_component_scores(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    identity: Option<&PilotIdentitySnapshot>,
) -> ComponentScores {
    let weights = &configuration.component_weights;

    ComponentScores {
        historical_capability: clamp_to_component_maximum(
            calculate_historical_capability_score(&configuration.historical_capability, statistics),
            weights.historical_capability.maximum_score,
        ),
        survivability: clamp_to_component_maximum(
            calculate_survivability_score(&configuration.survivability, statistics),
            weights.survivability.maximum_score,
        ),
        loss_quality: clamp_to_component_maximum(
            calculate_loss_quality_score(&configuration.loss_quality, statistics),
            weights.loss_quality.maximum_score,
        ),
        security: clamp_to_component_maximum(
            calculate_security_modifier(&configuration.security_status, identity),
            weights.security_status.maximum_score,
        ),
    }
}

pub(crate) fn is_threat_none(
    statistics: Option<&ZKillStatisticsSnapshot>,
    has_cached_killmails: bool,
) -> bool {
    match statistics {
        None => !has_cached_killmails,
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
    matches!(general_style, STYLE_VICTIM | STYLE_INACTIVE)
}

fn ratio_cap(
    configuration: &GatingConfiguration,
    statistics: &ZKillStatisticsSnapshot,
) -> Option<i32> {
    let ship_losses = statistics.ships_lost;

    if ship_losses <= 0 {
        return None;
    }

    let ratio = statistics.ships_destroyed as f64 / ship_losses as f64;

    if ratio <= configuration.medium_ratio_maximum {
        Some(configuration.kill_loss_cap_1)
    } else if ratio < configuration.high_ratio_below {
        Some(configuration.high_cap)
    } else {
        None
    }
}

pub(crate) fn evaluate_threat_gate(
    configuration: &GatingConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    has_cached_killmails: bool,
    recent_style: &str,
) -> ThreatGate {
    if is_threat_none(statistics, has_cached_killmails) {
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
    let cap = [
        style_cap,
        statistics.and_then(|value| ratio_cap(configuration, value)),
    ]
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
    let mut score = raw_score.round().clamp(0.0, THREAT_SCORE_MAXIMUM as f64) as i32;

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

    kill_volume_score + solo_kill_score + solo_ratio_score + style_score
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

    let ship_losses = statistics.ships_lost;

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

pub(crate) fn calculate_recent_activity_diagnostics(
    configuration: &RecentActivityConfiguration,
    maximum_score: i32,
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
    let score = interpolate_recent_activity_score(&configuration.points, daily_rate)
        .clamp(0.0, maximum_score.max(0) as f64);

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

fn style_matches(actual_code: &str, configured_style: &str) -> bool {
    actual_code == configured_style.trim().to_ascii_lowercase()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::contracts::RecentKillmailSnapshot;
    use crate::shared::recent_style_contract::{STYLE_GANG_BEGINNER, STYLE_SOLO_BEGINNER};
    use chrono::TimeZone;

    fn configuration() -> ThreatConfiguration {
        serde_json::from_str(
            crate::threat_analysis::threat_test_fixtures::THREAT_CONFIGURATION_JSON,
        )
        .expect("test threat configuration must parse")
    }

    fn now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 9, 22, 0, 0, 0).unwrap()
    }

    fn statistics(
        ships_destroyed: i32,
        ships_lost: i32,
        no_history_marker: bool,
    ) -> ZKillStatisticsSnapshot {
        ZKillStatisticsSnapshot {
            character_id: 1,
            ships_destroyed,
            solo_kills: 0,
            solo_ratio: 0.0,
            avg_gang_size: 0.0,
            ships_lost,
            solo_losses: 0,
            general_style: "unknown".to_string(),
            checked_at_utc: "2026-09-22T00:00:00Z".to_string(),
            no_history_marker,
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
            !recent_killmails.is_empty(),
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
        let result = analyze_intrinsic_threat(
            &configuration(),
            None,
            &[],
            false,
            None,
            None,
            now(),
            14,
            "inactive",
        );

        assert_eq!(result.score, 0);
    }

    #[test]
    fn no_history_marker_returns_none_score_even_with_combat_recent_style() {
        let stats = statistics(0, 0, true);
        let killmails = vec![killmail(false)];

        let result = analyze_intrinsic_threat(
            &configuration(),
            Some(&stats),
            &killmails,
            !killmails.is_empty(),
            None,
            None,
            now(),
            14,
            "solo",
        );

        assert_eq!(result.score, 0);
    }

    #[test]
    fn zero_statistics_kills_with_losses_scores_nothing_from_statistics_components() {
        let configuration = configuration();
        let stats = statistics(0, 5, false);

        assert_eq!(
            calculate_survivability_score(&configuration.survivability, Some(&stats)),
            0
        );
        assert_eq!(
            calculate_loss_quality_score(&configuration.loss_quality, Some(&stats)),
            0
        );
        assert_eq!(score(Some(&stats), &[], "inactive"), 0);
    }

    #[test]
    fn real_kill_history_returns_scored_result() {
        let stats = statistics(500, 50, false);

        assert!(score(Some(&stats), &[], "solo") > 0);
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
            !killmails.is_empty(),
            None,
            Some(coverage_start),
            now(),
            14,
            "gang",
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
            !killmails.is_empty(),
            None,
            Some(coverage_start),
            now(),
            14,
            "gang",
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
            !killmails.is_empty(),
            None,
            Some(coverage_start),
            now(),
            14,
            "solo",
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
            !killmails.is_empty(),
            None,
            Some(coverage_start),
            now(),
            14,
            "inactive",
        );

        assert_eq!(result.score, 0);
    }

    #[test]
    fn loss_quality_solo_loss_ratio_never_exceeds_one() {
        let configuration = configuration();
        let mut inflated = statistics(100, 20, false);
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
        let stats = statistics_with_style(1, 42, "victim");

        for recent in ["inactive", "victim", "unknown"] {
            assert_eq!(
                score(Some(&stats), &busy_recent_killmails(), recent),
                0,
                "{recent}"
            );
        }
    }

    #[test]
    fn general_inactive_with_recent_inactive_is_none() {
        let stats = statistics_with_style(0, 0, "inactive");

        assert_eq!(score(Some(&stats), &busy_recent_killmails(), "inactive"), 0);
    }

    #[test]
    fn general_victim_with_solo_recent_style_is_scored_capped_at_medium_and_never_none() {
        let stats = statistics_with_style(500, 10, "victim");

        let value = score(Some(&stats), &busy_recent_killmails(), "solo");

        assert!(value >= 1);
        assert!(value <= 40);
    }

    #[test]
    fn general_victim_style_cap_is_applied_to_a_high_raw_score() {
        let capped = statistics_with_style(5000, 1, "victim");
        let uncapped = statistics_with_style(5000, 1, "solo");

        assert_eq!(score(Some(&capped), &busy_recent_killmails(), "solo"), 40);
        assert!(score(Some(&uncapped), &busy_recent_killmails(), "solo") > 40);
    }

    #[test]
    fn general_inactive_zero_statistics_with_gang_recent_style_scores_from_recent_activity_and_security_only(
    ) {
        let stats = statistics_with_style(0, 0, "inactive");
        let killmails = busy_recent_killmails();
        let pilot = identity(Some(-6.0));

        let result = analyze_intrinsic_threat(
            &configuration(),
            Some(&stats),
            &killmails,
            !killmails.is_empty(),
            Some(&pilot),
            Some(now() - chrono::Duration::days(1)),
            now(),
            14,
            "gang",
        );

        assert_eq!(result.score, 15);
    }

    #[test]
    fn floor_gives_one_for_a_combat_recent_style_that_computes_zero() {
        let stats = statistics_with_style(0, 42, "victim");

        assert_eq!(score(Some(&stats), &[], "solo"), 1);
        assert_eq!(score(Some(&stats), &[], "fleet"), 1);
    }

    #[test]
    fn floor_is_not_applied_for_non_combat_recent_style_with_a_computed_zero() {
        let stats = statistics_with_style(0, 5, "unknown");

        assert_eq!(score(Some(&stats), &[], "victim"), 0);
    }

    #[test]
    fn ratio_caps_follow_the_configured_thresholds() {
        let configuration = configuration();
        let at_half = statistics_with_style(5000, 10000, "solo");
        let above_half = statistics_with_style(5100, 10000, "solo");
        let below_one = statistics_with_style(99, 100, "solo");
        let at_one = statistics_with_style(5000, 5000, "solo");
        let no_losses = statistics_with_style(5000, 0, "solo");

        assert_eq!(ratio_cap(&configuration.gating, &at_half), Some(40));
        assert_eq!(ratio_cap(&configuration.gating, &above_half), Some(60));
        assert_eq!(ratio_cap(&configuration.gating, &below_one), Some(60));
        assert_eq!(ratio_cap(&configuration.gating, &at_one), None);
        assert_eq!(ratio_cap(&configuration.gating, &no_losses), None);
    }

    #[test]
    fn high_volume_pilot_is_capped_by_ratio_and_uncapped_ratio_is_unchanged() {
        let killmails = busy_recent_killmails();
        let half = statistics_with_style(5000, 10000, "solo");
        let above_half = statistics_with_style(10000, 19000, "solo");
        let even = statistics_with_style(10000, 10000, "solo");

        assert_eq!(score(Some(&half), &killmails, "solo"), 40);
        assert_eq!(score(Some(&above_half), &killmails, "solo"), 60);
        assert!(score(Some(&even), &killmails, "solo") > 60);
    }

    #[test]
    fn lowest_applicable_cap_wins() {
        let configuration = configuration();
        let style_and_ratio = statistics_with_style(60, 100, "victim");
        let ratio_only = statistics_with_style(10, 100, "solo");

        let first =
            evaluate_threat_gate(&configuration.gating, Some(&style_and_ratio), false, "solo");
        let second = evaluate_threat_gate(&configuration.gating, Some(&ratio_only), false, "solo");

        assert_eq!(first.rule, GateRule::Capped);
        assert_eq!(first.cap, Some(40));
        assert_eq!(second.cap, Some(40));
    }

    #[test]
    fn low_volume_pilot_below_the_cap_is_unchanged() {
        let configuration = configuration();
        let stats = statistics_with_style(10, 100, "solo");
        let uncapped_total =
            calculate_historical_capability_score(
                &configuration.historical_capability,
                Some(&stats),
            ) + calculate_survivability_score(&configuration.survivability, Some(&stats))
                + calculate_loss_quality_score(&configuration.loss_quality, Some(&stats));

        assert!(uncapped_total < 40);
        assert_eq!(score(Some(&stats), &[], "solo"), uncapped_total);
    }

    #[test]
    fn statistics_missing_applies_no_general_style_or_ratio_gate() {
        let configuration = configuration();
        let gate = evaluate_threat_gate(&configuration.gating, None, true, "solo");

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

        assert_eq!(
            calculate_security_modifier(&configuration.security_status, None),
            0
        );
    }

    #[test]
    fn security_modifier_cases_from_the_specification() {
        let configuration = configuration();
        let cases = [
            (Some(0.1), 0),
            (Some(0.0), 0),
            (Some(-0.1), 1),
            (Some(-2.0), 1),
            (Some(-2.1), 3),
            (Some(-5.0), 3),
            (Some(-5.1), 5),
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
    }

    #[test]
    fn security_modifier_follows_the_configured_bands() {
        let mut configuration = configuration();
        configuration.security_status.bands[0].below_security_status = 1.0;
        configuration.security_status.bands[0].score = 2;
        let pilot = identity(Some(0.5));

        assert_eq!(
            calculate_security_modifier(&configuration.security_status, Some(&pilot)),
            2
        );
    }

    #[test]
    fn pilot_with_no_statistics_and_only_old_cached_killmails_is_not_none() {
        let coverage_start = Some(now() - chrono::Duration::days(1));
        let analyze = |has_cached: bool| {
            analyze_intrinsic_threat(
                &configuration(),
                None,
                &[],
                has_cached,
                None,
                coverage_start,
                now(),
                14,
                "solo",
            )
            .score
        };

        assert_eq!(analyze(false), 0);
        assert_eq!(analyze(true), 1);
    }

    #[test]
    fn none_gate_tests_all_cached_killmails_not_the_recent_window() {
        let configuration = configuration();

        assert_eq!(
            evaluate_threat_gate(&configuration.gating, None, false, "solo").rule,
            GateRule::NoData
        );
        assert_eq!(
            evaluate_threat_gate(&configuration.gating, None, true, "solo").rule,
            GateRule::Uncapped
        );
    }

    #[test]
    fn components_never_exceed_their_maximum_with_out_of_range_band_scores() {
        let mut configuration = configuration();
        configuration.historical_capability.kill_volume_bands[6].score = 90;
        configuration.survivability.ratios[5].score = 90;
        configuration.loss_quality.no_losses_with_kills_score = 90;
        configuration.security_status.bands[2].score = 90;
        configuration.recent_activity.points[4].score = 90.0;
        let stats = statistics_with_style(100_000, 0, "solo");
        let pilot = identity(Some(-9.0));

        let components = calculate_component_scores(&configuration, Some(&stats), Some(&pilot));
        let activity = calculate_recent_activity_diagnostics(
            &configuration.recent_activity,
            configuration
                .component_weights
                .recent_activity
                .maximum_score,
            &busy_recent_killmails(),
            Some(now() - chrono::Duration::days(1)),
            now(),
            14,
        );

        assert_eq!(components.historical_capability, 40);
        assert_eq!(components.survivability, 25);
        assert_eq!(components.loss_quality, 20);
        assert_eq!(components.security, 5);
        assert_eq!(activity.score, 10.0);
    }

    #[test]
    fn component_maxima_come_from_the_configuration() {
        let mut configuration = configuration();
        configuration
            .component_weights
            .historical_capability
            .maximum_score = 10;
        let stats = statistics_with_style(100_000, 0, "solo");

        let components = calculate_component_scores(&configuration, Some(&stats), None);

        assert_eq!(components.historical_capability, 10);
    }

    #[test]
    fn medium_cap_and_kill_loss_cap_1_are_independent() {
        let mut configuration = configuration();
        configuration.gating.medium_cap = 30;
        let victim = statistics_with_style(60, 100, "victim");
        let half_ratio = statistics_with_style(10, 100, "solo");

        let medium =
            evaluate_threat_gate(&configuration.gating, Some(&victim), false, STYLE_SOLO).cap;
        let kill_loss =
            evaluate_threat_gate(&configuration.gating, Some(&half_ratio), false, STYLE_SOLO).cap;

        assert_eq!(medium, Some(30));
        assert_eq!(kill_loss, Some(40));

        configuration.gating.medium_cap = 40;
        configuration.gating.kill_loss_cap_1 = 35;

        let medium =
            evaluate_threat_gate(&configuration.gating, Some(&victim), false, STYLE_SOLO).cap;
        let kill_loss =
            evaluate_threat_gate(&configuration.gating, Some(&half_ratio), false, STYLE_SOLO).cap;

        assert_eq!(medium, Some(40));
        assert_eq!(kill_loss, Some(35));
    }

    #[test]
    fn positive_security_status_never_returns_the_maximum() {
        let configuration = configuration();
        let pilot = identity(Some(5.0));

        assert_eq!(
            calculate_security_modifier(&configuration.security_status, Some(&pilot)),
            0
        );
    }

    const ALL_STYLE_CODES: [&str; 9] = [
        STYLE_UNKNOWN,
        STYLE_INACTIVE,
        STYLE_VICTIM,
        STYLE_SOLO,
        STYLE_SOLO_BEGINNER,
        STYLE_GANG,
        STYLE_GANG_BEGINNER,
        STYLE_BLOB,
        STYLE_FLEET,
    ];

    #[test]
    fn gate_for_every_general_and_recent_style_code_combination() {
        let configuration = configuration();

        for general in ALL_STYLE_CODES {
            for recent in ALL_STYLE_CODES {
                let stats = statistics_with_style(10, 5, general);
                let gate = evaluate_threat_gate(&configuration.gating, Some(&stats), false, recent);
                let general_non_combat = general == STYLE_VICTIM || general == STYLE_INACTIVE;
                let recent_non_combat =
                    recent == STYLE_VICTIM || recent == STYLE_INACTIVE || recent == STYLE_UNKNOWN;
                let label = format!("{general}/{recent}");

                if general_non_combat && recent_non_combat {
                    assert_eq!(gate.rule, GateRule::NonCombatNone, "{label}");
                    assert_eq!(gate.cap, None, "{label}");
                } else if general_non_combat {
                    assert_eq!(gate.rule, GateRule::Capped, "{label}");
                    assert_eq!(gate.cap, Some(configuration.gating.medium_cap), "{label}");
                } else {
                    assert_eq!(gate.rule, GateRule::Uncapped, "{label}");
                    assert_eq!(gate.cap, None, "{label}");
                }
            }
        }
    }

    #[test]
    fn display_text_is_not_a_style_code() {
        let configuration = configuration();

        for display in ["Vict", "Victim", "Inactive", "Solo", "Gang"] {
            let stats = statistics_with_style(10, 5, display);
            let gate = evaluate_threat_gate(&configuration.gating, Some(&stats), false, STYLE_SOLO);

            assert_eq!(gate.rule, GateRule::Uncapped, "{display}");
        }
    }

    #[test]
    fn solo_and_gang_beginners_receive_no_style_modifier() {
        let configuration = configuration();
        let modifier_score = |style: &str| {
            let stats = statistics_with_style(100_000, 0, style);

            calculate_historical_capability_score(
                &configuration.historical_capability,
                Some(&stats),
            )
        };
        let baseline = modifier_score(STYLE_UNKNOWN);

        assert!(modifier_score(STYLE_SOLO) > baseline);
        assert!(modifier_score(STYLE_GANG) > baseline);
        assert_eq!(modifier_score(STYLE_SOLO_BEGINNER), baseline);
        assert_eq!(modifier_score(STYLE_GANG_BEGINNER), baseline);
    }

    #[test]
    fn beginner_styles_use_the_default_loss_quality_bands() {
        let configuration = configuration();
        let loss_quality = |style: &str| {
            let mut stats = statistics_with_style(100, 10, style);
            stats.solo_losses = 10;

            calculate_loss_quality_score(&configuration.loss_quality, Some(&stats))
        };

        assert_eq!(
            loss_quality(STYLE_SOLO_BEGINNER),
            loss_quality(STYLE_UNKNOWN)
        );
        assert_eq!(
            loss_quality(STYLE_GANG_BEGINNER),
            loss_quality(STYLE_UNKNOWN)
        );
    }
}
