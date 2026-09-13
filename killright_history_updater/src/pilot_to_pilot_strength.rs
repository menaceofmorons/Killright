use chrono::{DateTime, Duration, Utc};
use duckdb::{params, Connection, Result};

use crate::confidence::{assign_confidence, Confidence, ConfidencePattern};
use crate::episode_builder::{build_same_c_a_episodes, fetch_pilot_affiliation_segments, is_same_c_a, AffiliationSegment, Episode};

pub const RECENCY_DECAY_SHORT_WINDOW_DAYS: i64 = 90;
pub const RECENCY_DECAY_LONG_WINDOW_DAYS: i64 = 180;
pub const RECENCY_VOLUME_MODIFIER_ONE_TIER_MIN_KILLS: i64 = 5;
pub const RECENCY_VOLUME_MODIFIER_TWO_TIER_MIN_KILLS: i64 = 10;

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum Strength {
    None,
    Weak,
    Medium,
    Strong,
    VeryStrong,
}

impl std::fmt::Display for Strength {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let text = match self {
            Strength::None => "None",
            Strength::Weak => "Weak",
            Strength::Medium => "Medium",
            Strength::Strong => "Strong",
            Strength::VeryStrong => "Very Strong",
        };
        write!(formatter, "{text}")
    }
}

pub fn is_currently_same_c_a(timeline_a: &[AffiliationSegment], timeline_b: &[AffiliationSegment]) -> bool {
    match (timeline_a.last(), timeline_b.last()) {
        (Some(segment_a), Some(segment_b)) => is_same_c_a(segment_a, segment_b),
        _ => false,
    }
}

pub fn classify_episode_count_without_recency(episode_count: usize) -> Option<(Strength, ConfidencePattern)> {
    match episode_count {
        0 => Some((Strength::Strong, ConfidencePattern::NeverSameCA)),
        n if n >= 3 => Some((Strength::VeryStrong, ConfidencePattern::ThreeOrMore)),
        _ => None,
    }
}

pub fn classify_recency_band(most_recent_post_evidence_utc: Option<DateTime<Utc>>, now: DateTime<Utc>) -> Strength {
    let short_boundary = now - Duration::days(RECENCY_DECAY_SHORT_WINDOW_DAYS);
    let long_boundary = now - Duration::days(RECENCY_DECAY_LONG_WINDOW_DAYS);

    match most_recent_post_evidence_utc {
        None => Strength::None,
        Some(timestamp) if timestamp < long_boundary => Strength::None,
        Some(timestamp) if timestamp < short_boundary => Strength::Weak,
        Some(_) => Strength::Medium,
    }
}

pub fn recency_volume_modifier_window(band: Strength, episode_end_utc: DateTime<Utc>, now: DateTime<Utc>) -> Option<(DateTime<Utc>, DateTime<Utc>)> {
    let short_boundary = now - Duration::days(RECENCY_DECAY_SHORT_WINDOW_DAYS);
    let long_boundary = now - Duration::days(RECENCY_DECAY_LONG_WINDOW_DAYS);

    match band {
        Strength::Weak => Some((long_boundary.max(episode_end_utc), short_boundary)),
        Strength::Medium => Some((short_boundary.max(episode_end_utc), now)),
        _ => None,
    }
}

pub fn apply_recency_volume_modifier(base: Strength, shared_kill_count_in_window: i64) -> Strength {
    let tier_bump = if shared_kill_count_in_window >= RECENCY_VOLUME_MODIFIER_TWO_TIER_MIN_KILLS {
        2
    } else if shared_kill_count_in_window >= RECENCY_VOLUME_MODIFIER_ONE_TIER_MIN_KILLS {
        1
    } else {
        0
    };

    match (base, tier_bump) {
        (Strength::Weak, 1) => Strength::Medium,
        (Strength::Weak, 2) => Strength::Strong,
        (Strength::Medium, 1) => Strength::Strong,
        (Strength::Medium, 2) => Strength::VeryStrong,
        _ => base,
    }
}

pub fn most_recent_shared_evidence_time(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, after_utc: DateTime<Utc>) -> Result<Option<DateTime<Utc>>> {
    let mut statement = connection.prepare(
        "SELECT MAX(e.killmail_time_utc) \
         FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id = ? AND e.killmail_time_utc > ?;",
    )?;

    let raw: Option<String> = statement.query_row(params![pilot_a_id, pilot_b_id, after_utc.to_rfc3339()], |row| row.get(0))?;

    Ok(raw.map(|text| {
        DateTime::parse_from_rfc3339(&text)
            .unwrap_or_else(|error| panic!("historic_relationship_evidence.killmail_time_utc is not valid RFC3339 ({text}): {error}"))
            .with_timezone(&Utc)
    }))
}

pub fn has_shared_evidence_between(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, start_exclusive_utc: DateTime<Utc>, end_exclusive_utc: DateTime<Utc>) -> Result<bool> {
    let mut statement = connection.prepare(
        "SELECT EXISTS ( \
             SELECT 1 FROM historic_relationship_evidence e \
             JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
             JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
             WHERE p1.character_id = ? AND p2.character_id = ? \
               AND e.killmail_time_utc > ? AND e.killmail_time_utc < ? \
         );",
    )?;

    statement.query_row(
        params![pilot_a_id, pilot_b_id, start_exclusive_utc.to_rfc3339(), end_exclusive_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

/// Design_Spec_Dense.md §6.11.4 Table53 (TwiceWithGap basis).
pub fn count_shared_evidence_between(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, start_exclusive_utc: DateTime<Utc>, end_exclusive_utc: DateTime<Utc>) -> Result<i64> {
    let mut statement = connection.prepare(
        "SELECT COUNT(*) FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id = ? \
           AND e.killmail_time_utc > ? AND e.killmail_time_utc < ?;",
    )?;

    statement.query_row(
        params![pilot_a_id, pilot_b_id, start_exclusive_utc.to_rfc3339(), end_exclusive_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

pub fn count_shared_evidence_in_window(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, window_start_utc: DateTime<Utc>, window_end_utc: DateTime<Utc>) -> Result<i64> {
    let mut statement = connection.prepare(
        "SELECT COUNT(*) FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id = ? \
           AND e.killmail_time_utc >= ? AND e.killmail_time_utc <= ?;",
    )?;

    statement.query_row(
        params![pilot_a_id, pilot_b_id, window_start_utc.to_rfc3339(), window_end_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

/// Design_Spec_Dense.md §6.11.4 Table53 (NeverSameCA basis).
pub fn count_all_shared_evidence(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64) -> Result<i64> {
    let mut statement = connection.prepare(
        "SELECT COUNT(*) FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id = ?;",
    )?;

    statement.query_row(params![pilot_a_id, pilot_b_id], |row| row.get(0))
}

pub fn classify_recency_decay(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, episode_end_utc: DateTime<Utc>, now: DateTime<Utc>) -> Result<(Strength, i64)> {
    let most_recent_post_evidence_utc = most_recent_shared_evidence_time(connection, pilot_a_id, pilot_b_id, episode_end_utc)?;
    let band = classify_recency_band(most_recent_post_evidence_utc, now);

    let window = match recency_volume_modifier_window(band, episode_end_utc, now) {
        Some(window) => window,
        None => return Ok((band, 0)),
    };

    let shared_kill_count_in_window = count_shared_evidence_in_window(connection, pilot_a_id, pilot_b_id, window.0, window.1)?;

    Ok((apply_recency_volume_modifier(band, shared_kill_count_in_window), shared_kill_count_in_window))
}

fn classify_exactly_two_episodes(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, episodes: &[Episode], now: DateTime<Utc>) -> Result<(Strength, Confidence)> {
    let first_episode = episodes[0];
    let second_episode = episodes[1];

    if has_shared_evidence_between(connection, pilot_a_id, pilot_b_id, first_episode.end_utc, second_episode.start_utc)? {
        let basis = count_shared_evidence_between(connection, pilot_a_id, pilot_b_id, first_episode.end_utc, second_episode.start_utc)?;
        return Ok((Strength::Strong, assign_confidence(ConfidencePattern::TwiceWithGap, basis)));
    }

    let (strength, basis) = classify_recency_decay(connection, pilot_a_id, pilot_b_id, second_episode.end_utc, now)?;
    Ok((strength, assign_confidence(ConfidencePattern::Recency, basis)))
}

fn classify_by_episode_count(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64, episodes: &[Episode], now: DateTime<Utc>) -> Result<(Strength, Confidence)> {
    if let Some((strength, pattern)) = classify_episode_count_without_recency(episodes.len()) {
        let basis = match pattern {
            ConfidencePattern::NeverSameCA => count_all_shared_evidence(connection, pilot_a_id, pilot_b_id)?,
            ConfidencePattern::ThreeOrMore => episodes.len() as i64,
            ConfidencePattern::Recency | ConfidencePattern::TwiceWithGap => unreachable!("classify_episode_count_without_recency never returns these patterns"),
        };
        return Ok((strength, assign_confidence(pattern, basis)));
    }

    if episodes.len() == 2 {
        classify_exactly_two_episodes(connection, pilot_a_id, pilot_b_id, episodes, now)
    } else {
        let (strength, basis) = classify_recency_decay(connection, pilot_a_id, pilot_b_id, episodes[0].end_utc, now)?;
        Ok((strength, assign_confidence(ConfidencePattern::Recency, basis)))
    }
}

pub fn classify_pilot_to_pilot_strength(connection: &Connection, pilot_a_id: i64, pilot_b_id: i64) -> Result<Option<(Strength, Confidence)>> {
    let timeline_a = fetch_pilot_affiliation_segments(connection, pilot_a_id)?;
    let timeline_b = fetch_pilot_affiliation_segments(connection, pilot_b_id)?;

    if is_currently_same_c_a(&timeline_a, &timeline_b) {
        return Ok(None);
    }

    let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);
    let now = Utc::now();

    classify_by_episode_count(connection, pilot_a_id, pilot_b_id, &episodes, now).map(Some)
}

#[cfg(test)]
mod tests {
    use chrono::TimeZone;

    use super::*;

    fn fixed_now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 1, 1, 0, 0, 0).unwrap()
    }

    fn days_before_fixed_now(days: i64) -> DateTime<Utc> {
        fixed_now() - Duration::days(days)
    }

    fn day(offset_days: i64) -> DateTime<Utc> {
        fixed_now() + Duration::days(offset_days)
    }

    fn segment(corporation_id: Option<i64>, alliance_id: Option<i64>, first_seen_day: i64, last_seen_day: i64) -> AffiliationSegment {
        AffiliationSegment {
            corporation_id,
            alliance_id,
            first_seen_utc: day(first_seen_day),
            last_seen_utc: day(last_seen_day),
        }
    }

    #[test]
    fn classify_episode_count_without_recency_none_for_one_or_two_episodes() {
        assert_eq!(classify_episode_count_without_recency(1), None);
        assert_eq!(classify_episode_count_without_recency(2), None);
    }

    #[test]
    fn classify_episode_count_without_recency_strong_for_zero_episodes() {
        assert_eq!(classify_episode_count_without_recency(0), Some((Strength::Strong, ConfidencePattern::NeverSameCA)));
    }

    #[test]
    fn classify_episode_count_without_recency_very_strong_for_three_or_more() {
        assert_eq!(classify_episode_count_without_recency(3), Some((Strength::VeryStrong, ConfidencePattern::ThreeOrMore)));
        assert_eq!(classify_episode_count_without_recency(9), Some((Strength::VeryStrong, ConfidencePattern::ThreeOrMore)));
    }

    #[test]
    fn is_currently_same_c_a_true_when_last_segments_match_on_corporation() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        assert!(is_currently_same_c_a(&timeline_a, &timeline_b));
    }

    #[test]
    fn is_currently_same_c_a_false_when_last_segments_differ() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];
        let timeline_b = vec![segment(Some(90379338), None, 0, 10)];

        assert!(!is_currently_same_c_a(&timeline_a, &timeline_b));
    }

    #[test]
    fn is_currently_same_c_a_ignores_an_earlier_matching_segment_when_the_last_segment_differs() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 5), segment(Some(90379338), None, 5, 10)];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        assert!(!is_currently_same_c_a(&timeline_a, &timeline_b));
    }

    #[test]
    fn is_currently_same_c_a_false_when_either_timeline_is_empty() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];

        assert!(!is_currently_same_c_a(&timeline_a, &[]));
        assert!(!is_currently_same_c_a(&[], &timeline_a));
    }

    #[test]
    fn classify_recency_band_none_when_no_post_evidence() {
        assert_eq!(classify_recency_band(None, fixed_now()), Strength::None);
    }

    #[test]
    fn classify_recency_band_none_beyond_six_months() {
        assert_eq!(classify_recency_band(Some(days_before_fixed_now(200)), fixed_now()), Strength::None);
    }

    #[test]
    fn classify_recency_band_weak_between_three_and_six_months() {
        assert_eq!(classify_recency_band(Some(days_before_fixed_now(120)), fixed_now()), Strength::Weak);
    }

    #[test]
    fn classify_recency_band_medium_within_three_months() {
        assert_eq!(classify_recency_band(Some(days_before_fixed_now(30)), fixed_now()), Strength::Medium);
    }

    #[test]
    fn classify_recency_band_exactly_ninety_days_is_medium() {
        assert_eq!(classify_recency_band(Some(days_before_fixed_now(RECENCY_DECAY_SHORT_WINDOW_DAYS)), fixed_now()), Strength::Medium);
    }

    #[test]
    fn classify_recency_band_exactly_one_hundred_eighty_days_is_weak() {
        assert_eq!(classify_recency_band(Some(days_before_fixed_now(RECENCY_DECAY_LONG_WINDOW_DAYS)), fixed_now()), Strength::Weak);
    }

    #[test]
    fn recency_volume_modifier_window_none_outside_weak_or_medium() {
        let episode_end = days_before_fixed_now(300);

        assert_eq!(recency_volume_modifier_window(Strength::None, episode_end, fixed_now()), None);
        assert_eq!(recency_volume_modifier_window(Strength::Strong, episode_end, fixed_now()), None);
        assert_eq!(recency_volume_modifier_window(Strength::VeryStrong, episode_end, fixed_now()), None);
    }

    #[test]
    fn recency_volume_modifier_window_weak_spans_the_three_to_six_month_band() {
        let episode_end = days_before_fixed_now(300);
        let window = recency_volume_modifier_window(Strength::Weak, episode_end, fixed_now()).unwrap();

        assert_eq!(window, (days_before_fixed_now(RECENCY_DECAY_LONG_WINDOW_DAYS), days_before_fixed_now(RECENCY_DECAY_SHORT_WINDOW_DAYS)));
    }

    #[test]
    fn recency_volume_modifier_window_medium_spans_from_the_three_month_boundary_to_now() {
        let episode_end = days_before_fixed_now(300);
        let window = recency_volume_modifier_window(Strength::Medium, episode_end, fixed_now()).unwrap();

        assert_eq!(window, (days_before_fixed_now(RECENCY_DECAY_SHORT_WINDOW_DAYS), fixed_now()));
    }

    #[test]
    fn recency_volume_modifier_window_clamps_start_to_episode_end() {
        let episode_end = days_before_fixed_now(100);
        let window = recency_volume_modifier_window(Strength::Weak, episode_end, fixed_now()).unwrap();

        assert_eq!(window.0, episode_end);
    }

    #[test]
    fn apply_recency_volume_modifier_no_bump_below_five_kills() {
        assert_eq!(apply_recency_volume_modifier(Strength::Weak, 4), Strength::Weak);
    }

    #[test]
    fn apply_recency_volume_modifier_one_tier_bump_from_five_to_nine_kills() {
        assert_eq!(apply_recency_volume_modifier(Strength::Weak, 5), Strength::Medium);
        assert_eq!(apply_recency_volume_modifier(Strength::Medium, 9), Strength::Strong);
    }

    #[test]
    fn apply_recency_volume_modifier_two_tier_bump_at_ten_or_more_kills() {
        assert_eq!(apply_recency_volume_modifier(Strength::Weak, 10), Strength::Strong);
        assert_eq!(apply_recency_volume_modifier(Strength::Medium, 10), Strength::VeryStrong);
    }

    #[test]
    fn apply_recency_volume_modifier_never_applies_to_none_strong_or_very_strong() {
        assert_eq!(apply_recency_volume_modifier(Strength::None, 500), Strength::None);
        assert_eq!(apply_recency_volume_modifier(Strength::Strong, 500), Strength::Strong);
        assert_eq!(apply_recency_volume_modifier(Strength::VeryStrong, 500), Strength::VeryStrong);
    }

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    fn insert_evidence(connection: &Connection, evidence_id: i64, killmail_time_utc: DateTime<Utc>) {
        connection
            .execute(
                "INSERT INTO historic_relationship_evidence \
                 (evidence_id, source_killmail_id, killmail_time_utc, evidence_date_utc, solar_system_id, \
                  victim_ship_type_id, participant_count, created_utc) \
                 VALUES (?, ?, ?, ?, NULL, NULL, 2, ?);",
                params![evidence_id, evidence_id, killmail_time_utc.to_rfc3339(), killmail_time_utc.date_naive().to_string(), killmail_time_utc.to_rfc3339()],
            )
            .unwrap();
    }

    fn insert_participant(connection: &Connection, evidence_id: i64, character_id: i64) {
        connection
            .execute(
                "INSERT INTO historic_relationship_evidence_participants \
                 (evidence_id, character_id, corporation_id, alliance_id, ship_type_id) \
                 VALUES (?, ?, NULL, NULL, NULL);",
                params![evidence_id, character_id],
            )
            .unwrap();
    }

    fn insert_shared_evidence(connection: &Connection, evidence_id: i64, killmail_time_utc: DateTime<Utc>, pilot_a_id: i64, pilot_b_id: i64) {
        insert_evidence(connection, evidence_id, killmail_time_utc);
        insert_participant(connection, evidence_id, pilot_a_id);
        insert_participant(connection, evidence_id, pilot_b_id);
    }

    fn insert_affiliation_segment(connection: &Connection, pilot_id: i64, corporation_id: Option<i64>, alliance_id: Option<i64>, first_seen_utc: DateTime<Utc>, last_seen_utc: DateTime<Utc>) {
        connection
            .execute(
                "INSERT INTO historic_pilot_affiliation_timeline \
                 (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
                 VALUES (?, ?, ?, ?, ?, ?);",
                params![pilot_id, corporation_id, alliance_id, first_seen_utc.to_rfc3339(), last_seen_utc.to_rfc3339(), last_seen_utc.to_rfc3339()],
            )
            .unwrap();
    }

    const PILOT_A: i64 = 95465499;
    const PILOT_B: i64 = 90379338;

    #[test]
    fn most_recent_shared_evidence_time_returns_the_latest_row_after_the_threshold() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(100), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 2, days_before_fixed_now(50), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 3, days_before_fixed_now(200), PILOT_A, PILOT_B);

        let result = most_recent_shared_evidence_time(&connection, PILOT_A, PILOT_B, days_before_fixed_now(150)).unwrap();

        assert_eq!(result, Some(days_before_fixed_now(50)));
    }

    #[test]
    fn most_recent_shared_evidence_time_returns_none_when_nothing_is_after_the_threshold() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(200), PILOT_A, PILOT_B);

        let result = most_recent_shared_evidence_time(&connection, PILOT_A, PILOT_B, days_before_fixed_now(100)).unwrap();

        assert_eq!(result, None);
    }

    #[test]
    fn most_recent_shared_evidence_time_ignores_evidence_involving_only_one_of_the_pair() {
        let connection = open_test_schema();
        insert_evidence(&connection, 1, days_before_fixed_now(10));
        insert_participant(&connection, 1, PILOT_A);
        insert_participant(&connection, 1, 111111);

        let result = most_recent_shared_evidence_time(&connection, PILOT_A, PILOT_B, days_before_fixed_now(100)).unwrap();

        assert_eq!(result, None);
    }

    #[test]
    fn has_shared_evidence_between_true_when_a_row_falls_strictly_inside_the_window() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(15), PILOT_A, PILOT_B);

        let result = has_shared_evidence_between(&connection, PILOT_A, PILOT_B, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert!(result);
    }

    #[test]
    fn has_shared_evidence_between_false_when_no_row_falls_inside_the_window() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(30), PILOT_A, PILOT_B);

        let result = has_shared_evidence_between(&connection, PILOT_A, PILOT_B, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert!(!result);
    }

    #[test]
    fn has_shared_evidence_between_false_on_the_boundary_timestamps_themselves() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(20), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 2, days_before_fixed_now(10), PILOT_A, PILOT_B);

        let result = has_shared_evidence_between(&connection, PILOT_A, PILOT_B, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert!(!result);
    }

    #[test]
    fn count_shared_evidence_in_window_counts_only_rows_inside_the_inclusive_window() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(20), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 2, days_before_fixed_now(15), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 3, days_before_fixed_now(10), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 4, days_before_fixed_now(25), PILOT_A, PILOT_B);

        let count = count_shared_evidence_in_window(&connection, PILOT_A, PILOT_B, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert_eq!(count, 3);
    }

    #[test]
    fn count_shared_evidence_between_counts_only_strictly_between_rows() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(20), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 2, days_before_fixed_now(15), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 3, days_before_fixed_now(10), PILOT_A, PILOT_B);

        let count = count_shared_evidence_between(&connection, PILOT_A, PILOT_B, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert_eq!(count, 1);
    }

    #[test]
    fn count_all_shared_evidence_counts_every_row_regardless_of_time() {
        let connection = open_test_schema();
        insert_shared_evidence(&connection, 1, days_before_fixed_now(500), PILOT_A, PILOT_B);
        insert_shared_evidence(&connection, 2, days_before_fixed_now(10), PILOT_A, PILOT_B);

        let count = count_all_shared_evidence(&connection, PILOT_A, PILOT_B).unwrap();

        assert_eq!(count, 2);
    }

    #[test]
    fn classify_recency_decay_none_when_no_post_evidence_exists() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::None);
        assert_eq!(basis, 0);
    }

    #[test]
    fn classify_recency_decay_weak_with_no_bump_below_five_shared_kills() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);
        insert_shared_evidence(&connection, 1, days_before_fixed_now(120), PILOT_A, PILOT_B);

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::Weak);
        assert_eq!(basis, 1);
    }

    #[test]
    fn classify_recency_decay_medium_with_no_bump_below_five_shared_kills() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);
        insert_shared_evidence(&connection, 1, days_before_fixed_now(30), PILOT_A, PILOT_B);

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::Medium);
        assert_eq!(basis, 1);
    }

    #[test]
    fn classify_recency_decay_weak_bumped_one_tier_to_medium_by_volume() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);

        for evidence_id in 0..6 {
            insert_shared_evidence(&connection, evidence_id, days_before_fixed_now(100 + evidence_id), PILOT_A, PILOT_B);
        }

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::Medium);
        assert_eq!(basis, 6);
    }

    #[test]
    fn classify_recency_decay_weak_bumped_two_tiers_to_strong_by_volume() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);

        for evidence_id in 0..10 {
            insert_shared_evidence(&connection, evidence_id, days_before_fixed_now(100 + evidence_id), PILOT_A, PILOT_B);
        }

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::Strong);
        assert_eq!(basis, 10);
    }

    #[test]
    fn classify_recency_decay_medium_bumped_two_tiers_to_very_strong_by_volume() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(400);

        for evidence_id in 0..10 {
            insert_shared_evidence(&connection, evidence_id, days_before_fixed_now(evidence_id), PILOT_A, PILOT_B);
        }

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::VeryStrong);
        assert_eq!(basis, 10);
    }

    #[test]
    fn classify_recency_decay_clamps_the_volume_window_to_episode_end_not_before() {
        let connection = open_test_schema();
        let episode_end = days_before_fixed_now(100);

        for evidence_id in 0..10 {
            insert_shared_evidence(&connection, evidence_id, days_before_fixed_now(150 + evidence_id), PILOT_A, PILOT_B);
        }
        insert_shared_evidence(&connection, 100, days_before_fixed_now(99), PILOT_A, PILOT_B);

        let (strength, basis) = classify_recency_decay(&connection, PILOT_A, PILOT_B, episode_end, fixed_now()).unwrap();

        assert_eq!(strength, Strength::Weak, "pre-split evidence must not feed the post-split Volume Modifier count");
        assert_eq!(basis, 1, "pre-split evidence must not feed the post-split Volume Modifier count");
    }

    fn ago(days: i64) -> DateTime<Utc> {
        Utc::now() - Duration::days(days)
    }

    #[test]
    fn classify_pilot_to_pilot_strength_none_when_currently_same_c_a() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(90111111), None, ago(400), ago(1));
        insert_affiliation_segment(&connection, PILOT_B, Some(90111111), None, ago(400), ago(1));

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), None);
    }

    #[test]
    fn classify_pilot_to_pilot_strength_strong_when_never_same_c_a() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_A, Some(90111111), None, ago(400), ago(1));
        insert_affiliation_segment(&connection, PILOT_B, Some(90222222), None, ago(400), ago(1));

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::Strong, Confidence::Low)));
    }

    #[test]
    fn classify_pilot_to_pilot_strength_very_strong_with_three_or_more_episodes() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_B, Some(90333333), None, ago(1060), ago(1000));

        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1060), ago(1050));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(1050), ago(1040));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT_A, Some(90555555), None, ago(1010), ago(1000));

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::VeryStrong, Confidence::Average)));
    }

    #[test]
    fn classify_pilot_to_pilot_strength_strong_with_exactly_two_episodes_and_between_episode_evidence() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_B, Some(90333333), None, ago(1040), ago(1000));

        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT_A, Some(90555555), None, ago(1010), ago(1000));

        insert_shared_evidence(&connection, 1, ago(1025), PILOT_A, PILOT_B);

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::Strong, Confidence::Low)));
    }

    #[test]
    fn classify_pilot_to_pilot_strength_collapses_two_episodes_without_between_episode_evidence_and_decays() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_B, Some(90333333), None, ago(1040), ago(1000));

        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT_A, Some(90555555), None, ago(1010), ago(1000));

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::None, Confidence::Low)));
    }

    #[test]
    fn classify_pilot_to_pilot_strength_exactly_one_episode_recency_decays_to_medium() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT_B, Some(90333333), None, ago(1000), ago(400));
        insert_affiliation_segment(&connection, PILOT_A, Some(90333333), None, ago(1000), ago(400));
        insert_affiliation_segment(&connection, PILOT_A, Some(90444444), None, ago(400), ago(1));

        insert_shared_evidence(&connection, 1, ago(30), PILOT_A, PILOT_B);

        assert_eq!(classify_pilot_to_pilot_strength(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::Medium, Confidence::Low)));
    }
}
