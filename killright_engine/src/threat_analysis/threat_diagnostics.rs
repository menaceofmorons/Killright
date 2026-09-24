use chrono::{DateTime, Utc};

use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;
use crate::repositories::recent_killmail_repository::RecentKillmailSnapshot;
use crate::repositories::zkill_statistics_repository::ZKillStatisticsSnapshot;
use crate::threat_analysis::threat_analyzer::{
    calculate_confidence, calculate_historical_capability_score, calculate_loss_quality_score,
    calculate_recent_activity_diagnostics, calculate_security_modifier, calculate_survivability_score,
    is_threat_none,
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
    pub confidence: i32,
    pub coverage_start_present: bool,
    pub observed_days: f64,
    pub counted_kills: i64,
    pub daily_rate: f64,
}

pub fn analyze_intrinsic_threat_diagnostics(
    configuration: &ThreatConfiguration,
    statistics: Option<&ZKillStatisticsSnapshot>,
    recent_killmails: &[RecentKillmailSnapshot],
    identity: Option<&PilotIdentitySnapshot>,
    coverage_start_utc: Option<DateTime<Utc>>,
    now: DateTime<Utc>,
    recent_window_days: i64,
) -> ThreatDiagnostics {
    let confidence = calculate_confidence(&configuration.confidence, statistics, recent_killmails);

    let recent_activity = calculate_recent_activity_diagnostics(
        &configuration.recent_activity,
        recent_killmails,
        coverage_start_utc,
        now,
        recent_window_days,
    );

    if is_threat_none(statistics, recent_killmails) {
        return ThreatDiagnostics {
            historical_capability: 0,
            survivability: 0,
            loss_quality: 0,
            recent_activity_modifier: 0.0,
            security_modifier: 0,
            score: 0,
            confidence,
            coverage_start_present: recent_activity.coverage_start_present,
            observed_days: recent_activity.observed_days,
            counted_kills: recent_activity.counted_kills,
            daily_rate: recent_activity.daily_rate,
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

    let score = raw_score.round().clamp(0.0, 100.0) as i32;

    ThreatDiagnostics {
        historical_capability,
        survivability,
        loss_quality,
        recent_activity_modifier: recent_activity.score,
        security_modifier,
        score,
        confidence,
        coverage_start_present: recent_activity.coverage_start_present,
        observed_days: recent_activity.observed_days,
        counted_kills: recent_activity.counted_kills,
        daily_rate: recent_activity.daily_rate,
    }
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

    #[test]
    fn none_pilot_returns_zeroed_components_with_formula_confidence() {
        let stats = statistics(0, 0, true);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14);

        assert_eq!(diagnostics.score, 0);
        assert_eq!(diagnostics.historical_capability, 0);
        assert_eq!(diagnostics.survivability, 0);
        assert_eq!(diagnostics.loss_quality, 0);
        assert_eq!(diagnostics.security_modifier, 0);
        assert_eq!(diagnostics.confidence, 100);
    }

    #[test]
    fn real_kill_history_components_sum_to_reported_score() {
        let stats = statistics(500, 50, false);

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14);

        let expected_score = (diagnostics.historical_capability as f64
            + diagnostics.survivability as f64
            + diagnostics.loss_quality as f64
            + diagnostics.recent_activity_modifier
            + diagnostics.security_modifier as f64)
            .round()
            .clamp(0.0, 100.0) as i32;

        assert_eq!(diagnostics.score, expected_score);
        assert_eq!(diagnostics.confidence, 90);
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

        let diagnostics = analyze_intrinsic_threat_diagnostics(&configuration(), Some(&stats), &[], None, None, now(), 14);

        assert!(!diagnostics.coverage_start_present);
        assert_eq!(diagnostics.recent_activity_modifier, 0.0);
    }
}
