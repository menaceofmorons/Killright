use chrono::{DateTime, Utc};

use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;
use crate::repositories::recent_killmail_repository::RecentKillmailSnapshot;
use crate::repositories::zkill_statistics_repository::ZKillStatisticsSnapshot;
use crate::threat_analysis::threat_analyzer::{
    calculate_historical_capability_score, calculate_loss_quality_score,
    calculate_recent_activity_diagnostics, calculate_security_modifier, calculate_survivability_score,
    evaluate_threat_gate, finalize_threat_score, GateRule,
};
use crate::threat_analysis::threat_configuration_models::ThreatConfiguration;

#[derive(Debug, Clone, PartialEq)]
pub struct ThreatDiagnostics {
    pub historical_capability: i32,
    pub survivability: i32,
    pub loss_quality: i32,
    pub recent_activity_modifier: f64,
    pub security_modifier: i32,
    pub score: i32,
    pub coverage_start_present: bool,
    pub observed_days: f64,
    pub counted_kills: i64,
    pub daily_rate: f64,
    pub gate_rule: String,
    pub gate_cap: Option<i32>,
    pub floor_applied: bool,
}

pub fn analyze_intrinsic_threat_diagnostics(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    identity: Option<&PilotIdentitySnapshot>,
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
    recent_style: &str,
) -> ThreatDiagnostics {
    let recent_activity = calculate_recent_activity_diagnostics(
        &configuration.recent_activity,
        recent_killmails,
        coverage_start_utc,
        now,
        recent_window_days,
    );

    let gate = evaluate_threat_gate(&configuration.gating, statistics, recent_killmails, recent_style);

    if matches!(gate.rule, GateRule::NoData | GateRule::NonCombatNone) {
        return ThreatDiagnostics {
            historical_capability: 0,
            survivability: 0,
            loss_quality: 0,
            recent_activity_modifier: 0.0,
            security_modifier: 0,
            score: 0,
            coverage_start_present: recent_activity.coverage_start_present,
            observed_days: recent_activity.observed_days,
            counted_kills: recent_activity.counted_kills,
            daily_rate: recent_activity.daily_rate,
            gate_rule: gate.rule.as_str().to_string(),
            gate_cap: None,
            floor_applied: false,
        };
    }

    let historical_capability =
        calculate_historical_capability_score(&configuration.historical_capability, statistics);
    let survivability = calculate_survivability_score(&configuration.survivability, statistics);
    let loss_quality = calculate_loss_quality_score(&configuration.loss_quality, statistics);
    let security_modifier = calculate_security_modifier(&configuration.security_status, identity);

    let raw_score = historical_capability as f64
        + survivability as f64
        + loss_quality as f64
        + recent_activity.score
        + security_modifier as f64;

    let (score, floor_applied) =
        finalize_threat_score(&configuration.gating, &gate, raw_score, recent_style);

    ThreatDiagnostics {
        historical_capability,
        survivability,
        loss_quality,
        recent_activity_modifier: recent_activity.score,
        security_modifier,
        score,
        coverage_start_present: recent_activity.coverage_start_present,
        observed_days: recent_activity.observed_days,
        counted_kills: recent_activity.counted_kills,
        daily_rate: recent_activity.daily_rate,
        gate_rule: gate.rule.as_str().to_string(),
        gate_cap: gate.cap,
        floor_applied,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::repositories::recent_killmail_repository::RecentKillmailSnapshot;
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

    #[test]
    fn none_pilot_returns_zeroed_components() {
        let stats = statistics(0, 0, true);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Solo");

        assert_eq!(diagnostics.score, 0);
        assert_eq!(diagnostics.historical_capability, 0);
        assert_eq!(diagnostics.survivability, 0);
        assert_eq!(diagnostics.loss_quality, 0);
        assert_eq!(diagnostics.security_modifier, 0);
    }

    #[test]
    fn real_kill_history_components_sum_to_reported_score() {
        let stats = statistics(500, 50, false);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Solo");

        let expected_score = (diagnostics.historical_capability as f64
            + diagnostics.survivability as f64
            + diagnostics.loss_quality as f64
            + diagnostics.recent_activity_modifier
            + diagnostics.security_modifier as f64)
            .round()
            .clamp(0.0, 100.0) as i32;

        assert_eq!(diagnostics.score, expected_score);
    }

    #[test]
    fn recent_activity_fields_report_coverage_and_daily_rate() {
        let coverage_start = now() - chrono::Duration::days(2);
        let killmails = vec![
            killmail_at(true, "2026-09-21T00:00:00Z"),
            killmail_at(false, "2026-09-19T00:00:00Z"),
            killmail_at(false, "2026-09-21T00:00:00Z"),
        ];

        let diagnostics = analyze_intrinsic_threat_diagnostics(
            &configuration(),
            None,
            &killmails,
            None,
            Some(coverage_start),
            now(),
            14,
            "Solo",
        );

        assert!(diagnostics.coverage_start_present);
        assert!((diagnostics.observed_days - 2.0).abs() < 1e-9);
        assert_eq!(diagnostics.counted_kills, 1);
        assert!((diagnostics.daily_rate - 0.5).abs() < 1e-9);
        assert_eq!(diagnostics.score, 1);
    }

    #[test]
    fn missing_coverage_start_reports_not_present() {
        let stats = statistics(10, 0, false);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Solo");

        assert!(!diagnostics.coverage_start_present);
        assert_eq!(diagnostics.recent_activity_modifier, 0.0);
    }

    fn statistics_with_style(ships_destroyed: i32, ships_lost: i32, general_style: &str) -> ZKillStatisticsSnapshot {
        let mut value = statistics(ships_destroyed, ships_lost, false);
        value.general_style = general_style.to_string();
        value
    }

    #[test]
    fn non_combat_gate_reports_rule_and_zeroed_components() {
        let stats = statistics_with_style(1, 42, "Vict");

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Inactive");

        assert_eq!(diagnostics.score, 0);
        assert_eq!(diagnostics.gate_rule, "non_combat_none");
        assert_eq!(diagnostics.gate_cap, None);
        assert!(!diagnostics.floor_applied);
    }

    #[test]
    fn no_history_reports_no_data_rule() {
        let stats = statistics(0, 0, true);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Solo");

        assert_eq!(diagnostics.gate_rule, "no_data");
    }

    #[test]
    fn capped_gate_reports_cap_and_matches_the_analyzer_score() {
        let stats = statistics_with_style(5000, 10000, "Solo");
        let killmails: Vec<RecentKillmailSnapshot> = (0..20)
            .map(|_| killmail_at(false, "2026-09-21T12:00:00Z"))
            .collect();
        let coverage_start = Some(now() - chrono::Duration::days(1));

        let diagnostics = analyze_intrinsic_threat_diagnostics(
            &configuration(),
            Some(&stats),
            &killmails,
            None,
            coverage_start,
            now(),
            14,
            "Solo",
        );
        let analyzed = crate::threat_analysis::threat_analyzer::analyze_intrinsic_threat(
            &configuration(),
            Some(&stats),
            &killmails,
            None,
            coverage_start,
            now(),
            14,
            "Solo",
        );

        assert_eq!(diagnostics.gate_rule, "capped");
        assert_eq!(diagnostics.gate_cap, Some(40));
        assert_eq!(diagnostics.score, 40);
        assert_eq!(diagnostics.score, analyzed.score);
    }

    #[test]
    fn floor_applied_is_reported() {
        let stats = statistics_with_style(0, 42, "Vict");

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14, "Gang");

        assert_eq!(diagnostics.score, 1);
        assert!(diagnostics.floor_applied);
        assert_eq!(diagnostics.gate_cap, Some(40));
    }
}
