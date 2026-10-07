use chrono::{DateTime, Duration, Utc};

use crate::contracts::RecentKillmailSnapshot;
use crate::shared::pod_kill::is_pod_kill;

pub const WEEKLY_WINDOW_DAYS: i64 = 7;

#[derive(Debug, Clone, PartialEq)]
pub struct NewestKillmail {
    pub kill_time_utc: String,
    pub is_loss: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DerivedActivity {
    pub has_public_activity_data: bool,
    pub kills_week: i32,
    pub solo_week: i32,
    pub info_week_losses: i32,
    pub newest_non_pod_killmail: Option<NewestKillmail>,
    pub newest_non_pod_kill_time_utc: Option<String>,
}

pub fn derive_activity(killmails: &[RecentKillmailSnapshot], now: DateTime<Utc>) -> DerivedActivity {
    let cutoff = now - Duration::days(WEEKLY_WINDOW_DAYS);

    let mut window = killmails
        .iter()
        .filter_map(|row| {
            DateTime::parse_from_rfc3339(&row.kill_time_utc)
                .ok()
                .map(|timestamp| (timestamp.with_timezone(&Utc), row))
        })
        .filter(|(timestamp, _)| *timestamp >= cutoff)
        .collect::<Vec<_>>();

    window.sort_by(|left, right| right.0.cmp(&left.0));

    let newest_non_pod_kill_time_utc = killmails
        .iter()
        .filter(|row| !row.is_loss && !is_pod_kill(row.ship_type_id))
        .filter_map(|row| {
            DateTime::parse_from_rfc3339(&row.kill_time_utc)
                .ok()
                .map(|timestamp| (timestamp.with_timezone(&Utc), row))
        })
        .max_by_key(|(timestamp, _)| *timestamp)
        .map(|(_, row)| row.kill_time_utc.clone());

    let mut derived = DerivedActivity {
        has_public_activity_data: !window.is_empty(),
        kills_week: 0,
        solo_week: 0,
        info_week_losses: 0,
        newest_non_pod_killmail: None,
        newest_non_pod_kill_time_utc,
    };

    for (_, row) in window {
        let is_pod_kill_row = !row.is_loss && is_pod_kill(row.ship_type_id);

        if row.is_loss {
            derived.info_week_losses += 1;
        }

        if derived.newest_non_pod_killmail.is_none() && !is_pod_kill_row {
            derived.newest_non_pod_killmail = Some(NewestKillmail {
                kill_time_utc: row.kill_time_utc.clone(),
                is_loss: row.is_loss,
            });
        }

        if !row.is_loss && !is_pod_kill_row {
            derived.kills_week += 1;

            if row.is_solo {
                derived.solo_week += 1;
            }
        }
    }

    derived
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 9, 29, 12, 0, 0).unwrap()
    }

    fn row(killmail_id: i64, kill_time_utc: &str, is_loss: bool, is_solo: bool, ship_type_id: Option<i64>) -> RecentKillmailSnapshot {
        RecentKillmailSnapshot {
            killmail_id,
            killmail_hash: None,
            character_id: 95465499,
            kill_time_utc: kill_time_utc.to_string(),
            is_loss,
            attacker_count: if is_solo { 1 } else { 3 },
            is_solo,
            ship_type_id,
            system_id: Some(30000142),
            location_id: None,
            is_npc: false,
            cached_at_utc: kill_time_utc.to_string(),
        }
    }

    #[test]
    fn no_rows_yields_no_public_activity() {
        let derived = derive_activity(&[], now());

        assert!(!derived.has_public_activity_data);
        assert_eq!(derived.kills_week, 0);
        assert_eq!(derived.solo_week, 0);
        assert!(derived.newest_non_pod_killmail.is_none());
        assert!(derived.newest_non_pod_kill_time_utc.is_none());
    }

    #[test]
    fn rows_outside_the_weekly_window_are_ignored() {
        let rows = vec![row(1, "2026-09-10T00:00:00+00:00", false, true, Some(587))];

        let derived = derive_activity(&rows, now());

        assert!(!derived.has_public_activity_data);
        assert!(derived.newest_non_pod_killmail.is_none());
    }

    #[test]
    fn counts_kills_and_solo_kills_inside_the_window() {
        let rows = vec![
            row(1, "2026-09-28T00:00:00+00:00", false, true, Some(587)),
            row(2, "2026-09-27T00:00:00+00:00", false, false, Some(587)),
            row(3, "2026-09-26T00:00:00+00:00", false, true, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert!(derived.has_public_activity_data);
        assert_eq!(derived.kills_week, 3);
        assert_eq!(derived.solo_week, 2);
    }

    #[test]
    fn pod_kills_are_excluded_from_counts_and_newest() {
        let rows = vec![
            row(1, "2026-09-28T12:00:00+00:00", false, true, Some(670)),
            row(2, "2026-09-28T00:00:00+00:00", false, true, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert!(derived.has_public_activity_data);
        assert_eq!(derived.kills_week, 1);
        assert_eq!(derived.solo_week, 1);
        assert_eq!(
            derived.newest_non_pod_killmail,
            Some(NewestKillmail {
                kill_time_utc: "2026-09-28T00:00:00+00:00".to_string(),
                is_loss: false,
            })
        );
        assert_eq!(derived.newest_non_pod_kill_time_utc.as_deref(), Some("2026-09-28T00:00:00+00:00"));
    }

    #[test]
    fn newest_killmail_may_be_a_loss_and_a_pod_loss_counts() {
        let rows = vec![
            row(1, "2026-09-28T12:00:00+00:00", true, false, Some(670)),
            row(2, "2026-09-28T00:00:00+00:00", false, false, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(
            derived.newest_non_pod_killmail,
            Some(NewestKillmail {
                kill_time_utc: "2026-09-28T12:00:00+00:00".to_string(),
                is_loss: true,
            })
        );
        assert_eq!(derived.kills_week, 1);
        assert_eq!(derived.newest_non_pod_kill_time_utc.as_deref(), Some("2026-09-28T00:00:00+00:00"));
    }

    #[test]
    fn losses_do_not_count_toward_kills_week() {
        let rows = vec![row(1, "2026-09-28T00:00:00+00:00", true, true, Some(587))];

        let derived = derive_activity(&rows, now());

        assert!(derived.has_public_activity_data);
        assert_eq!(derived.kills_week, 0);
        assert_eq!(derived.solo_week, 0);
        assert!(derived.newest_non_pod_kill_time_utc.is_none());
    }

    #[test]
    fn newest_kill_outside_the_weekly_window_is_still_reported() {
        let rows = vec![row(1, "2026-09-09T00:00:00+00:00", false, true, Some(587))];

        let derived = derive_activity(&rows, now());

        assert!(!derived.has_public_activity_data);
        assert_eq!(derived.kills_week, 0);
        assert!(derived.newest_non_pod_killmail.is_none());
        assert_eq!(derived.newest_non_pod_kill_time_utc.as_deref(), Some("2026-09-09T00:00:00+00:00"));
    }

    #[test]
    fn old_kill_and_recent_loss_report_the_kill_and_the_loss_separately() {
        let rows = vec![
            row(1, "2026-09-09T00:00:00+00:00", false, false, Some(587)),
            row(2, "2026-09-28T00:00:00+00:00", true, false, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(derived.newest_non_pod_kill_time_utc.as_deref(), Some("2026-09-09T00:00:00+00:00"));
        assert_eq!(
            derived.newest_non_pod_killmail,
            Some(NewestKillmail {
                kill_time_utc: "2026-09-28T00:00:00+00:00".to_string(),
                is_loss: true,
            })
        );
        assert_eq!(derived.kills_week, 0);
    }

    #[test]
    fn pod_kills_and_losses_are_skipped_over_the_whole_history() {
        let rows = vec![
            row(1, "2026-08-01T00:00:00+00:00", false, true, Some(33328)),
            row(2, "2026-08-02T00:00:00+00:00", false, true, Some(670)),
            row(3, "2026-08-03T00:00:00+00:00", true, true, Some(587)),
            row(4, "2026-07-15T00:00:00+00:00", false, false, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(derived.newest_non_pod_kill_time_utc.as_deref(), Some("2026-07-15T00:00:00+00:00"));
    }

    #[test]
    fn only_losses_over_the_whole_history_give_no_newest_kill() {
        let rows = vec![
            row(1, "2026-08-03T00:00:00+00:00", true, true, Some(587)),
            row(2, "2026-09-28T00:00:00+00:00", true, false, Some(670)),
        ];

        let derived = derive_activity(&rows, now());

        assert!(derived.newest_non_pod_kill_time_utc.is_none());
    }

    #[test]
    fn input_order_does_not_change_the_result() {
        let ascending = vec![
            row(1, "2026-09-26T00:00:00+00:00", false, true, Some(587)),
            row(2, "2026-09-28T00:00:00+00:00", true, false, Some(587)),
        ];
        let mut descending = ascending.clone();
        descending.reverse();

        assert_eq!(derive_activity(&ascending, now()), derive_activity(&descending, now()));
    }

    #[test]
    fn info_week_losses_include_pod_losses_and_exclude_kills() {
        let rows = vec![
            row(1, "2026-09-28T12:00:00+00:00", false, true, Some(670)),
            row(2, "2026-09-28T06:00:00+00:00", false, false, Some(587)),
            row(3, "2026-09-28T03:00:00+00:00", true, false, Some(670)),
            row(4, "2026-09-27T00:00:00+00:00", true, true, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(derived.info_week_losses, 2);
        assert_eq!(derived.kills_week, 1);
        assert_eq!(derived.solo_week, 0);
    }

    #[test]
    fn info_week_losses_respect_the_seven_day_boundary() {
        let rows = vec![
            row(1, "2026-09-22T12:00:00+00:00", true, false, Some(670)),
            row(2, "2026-09-22T11:59:59+00:00", true, false, Some(670)),
            row(3, "2026-09-22T12:00:00+00:00", false, true, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(derived.info_week_losses, 1);
    }

    #[test]
    fn grid_week_values_are_unchanged_by_info_week_losses() {
        let rows = vec![
            row(1, "2026-09-28T00:00:00+00:00", false, true, Some(587)),
            row(2, "2026-09-27T00:00:00+00:00", false, false, Some(587)),
            row(3, "2026-09-26T00:00:00+00:00", false, true, Some(670)),
            row(4, "2026-09-25T00:00:00+00:00", true, false, Some(587)),
        ];

        let derived = derive_activity(&rows, now());

        assert_eq!(derived.kills_week, 2);
        assert_eq!(derived.solo_week, 1);
        assert_eq!(derived.info_week_losses, 1);
    }
}
