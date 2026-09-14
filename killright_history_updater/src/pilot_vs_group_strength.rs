use chrono::{DateTime, Utc};
use duckdb::{params, Connection, Result};

use crate::confidence::{assign_confidence, Confidence, ConfidencePattern};
use crate::episode_builder::{build_same_c_a_episodes, fetch_pilot_affiliation_segments, fixed_group_segment, AffiliationSegment, Episode};
use crate::esi_active_status::{ensure_alliance_active_status_cached, ensure_corporation_active_status_cached, EntityType, EsiActiveStatusClient};
use crate::pilot_to_pilot_strength::{apply_recency_volume_modifier, classify_recency_band, is_currently_same_c_a, recency_volume_modifier_window, Strength};

// Design_Spec_Dense.md §6.11.4 Pilot-vs-Group extension.
fn group_affiliation_segment(entity_type: EntityType, entity_id: i64) -> AffiliationSegment {
    match entity_type {
        EntityType::Corporation => fixed_group_segment(Some(entity_id), None),
        EntityType::Alliance => fixed_group_segment(None, Some(entity_id)),
    }
}

fn alongside_column(entity_type: EntityType) -> &'static str {
    match entity_type {
        EntityType::Corporation => "corporation_id",
        EntityType::Alliance => "alliance_id",
    }
}

pub fn most_recent_alongside_evidence_time(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    after_utc: DateTime<Utc>,
) -> Result<Option<DateTime<Utc>>> {
    let column = alongside_column(entity_type);
    let sql = format!(
        "SELECT MAX(e.killmail_time_utc) \
         FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id != p1.character_id AND p2.{column} = ? \
           AND e.killmail_time_utc > ?;"
    );

    let mut statement = connection.prepare(&sql)?;
    let raw: Option<String> = statement.query_row(params![pilot_id, entity_id, after_utc.to_rfc3339()], |row| row.get(0))?;

    Ok(raw.map(|text| {
        DateTime::parse_from_rfc3339(&text)
            .unwrap_or_else(|error| panic!("historic_relationship_evidence.killmail_time_utc is not valid RFC3339 ({text}): {error}"))
            .with_timezone(&Utc)
    }))
}

pub fn has_alongside_evidence_between(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    start_exclusive_utc: DateTime<Utc>,
    end_exclusive_utc: DateTime<Utc>,
) -> Result<bool> {
    let column = alongside_column(entity_type);
    let sql = format!(
        "SELECT EXISTS ( \
             SELECT 1 FROM historic_relationship_evidence e \
             JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
             JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
             WHERE p1.character_id = ? AND p2.character_id != p1.character_id AND p2.{column} = ? \
               AND e.killmail_time_utc > ? AND e.killmail_time_utc < ? \
         );"
    );

    let mut statement = connection.prepare(&sql)?;
    statement.query_row(
        params![pilot_id, entity_id, start_exclusive_utc.to_rfc3339(), end_exclusive_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

// Design_Spec_Dense.md §6.11.4 Table53 (TwiceWithGap basis), Pilot-vs-Group.
pub fn count_alongside_evidence_between(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    start_exclusive_utc: DateTime<Utc>,
    end_exclusive_utc: DateTime<Utc>,
) -> Result<i64> {
    let column = alongside_column(entity_type);
    let sql = format!(
        "SELECT COUNT(*) FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id != p1.character_id AND p2.{column} = ? \
           AND e.killmail_time_utc > ? AND e.killmail_time_utc < ?;"
    );

    let mut statement = connection.prepare(&sql)?;
    statement.query_row(
        params![pilot_id, entity_id, start_exclusive_utc.to_rfc3339(), end_exclusive_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

pub fn count_alongside_evidence_in_window(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    window_start_utc: DateTime<Utc>,
    window_end_utc: DateTime<Utc>,
) -> Result<i64> {
    let column = alongside_column(entity_type);
    let sql = format!(
        "SELECT COUNT(*) FROM historic_relationship_evidence e \
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id \
         JOIN historic_relationship_evidence_participants p2 ON p2.evidence_id = e.evidence_id \
         WHERE p1.character_id = ? AND p2.character_id != p1.character_id AND p2.{column} = ? \
           AND e.killmail_time_utc >= ? AND e.killmail_time_utc <= ?;"
    );

    let mut statement = connection.prepare(&sql)?;
    statement.query_row(
        params![pilot_id, entity_id, window_start_utc.to_rfc3339(), window_end_utc.to_rfc3339()],
        |row| row.get(0),
    )
}

pub fn classify_recency_decay_for_group(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    episode_end_utc: DateTime<Utc>,
    now: DateTime<Utc>,
) -> Result<(Strength, i64)> {
    let most_recent_post_evidence_utc = most_recent_alongside_evidence_time(connection, pilot_id, entity_type, entity_id, episode_end_utc)?;
    let band = classify_recency_band(most_recent_post_evidence_utc, now);

    let window = match recency_volume_modifier_window(band, episode_end_utc, now) {
        Some(window) => window,
        None => return Ok((band, 0)),
    };

    let shared_kill_count_in_window = count_alongside_evidence_in_window(connection, pilot_id, entity_type, entity_id, window.0, window.1)?;

    Ok((apply_recency_volume_modifier(band, shared_kill_count_in_window), shared_kill_count_in_window))
}

fn classify_exactly_two_episodes_for_group(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    episodes: &[Episode],
    now: DateTime<Utc>,
) -> Result<(Strength, Confidence)> {
    let first_episode = episodes[0];
    let second_episode = episodes[1];

    if has_alongside_evidence_between(connection, pilot_id, entity_type, entity_id, first_episode.end_utc, second_episode.start_utc)? {
        let basis = count_alongside_evidence_between(connection, pilot_id, entity_type, entity_id, first_episode.end_utc, second_episode.start_utc)?;
        return Ok((Strength::Strong, assign_confidence(ConfidencePattern::TwiceWithGap, basis)));
    }

    let (strength, basis) = classify_recency_decay_for_group(connection, pilot_id, entity_type, entity_id, second_episode.end_utc, now)?;
    Ok((strength, assign_confidence(ConfidencePattern::Recency, basis)))
}

// Design_Spec_Dense.md §6.11.4: unlike Pilot-to-Pilot's zero-episode case
// (never same c/a, ever -> Strong), classify_pilot_vs_group_strength never
// calls this with zero episodes -- it returns None first ("no membership to
// anchor a row to").
fn classify_by_episode_count_for_group(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    episodes: &[Episode],
    now: DateTime<Utc>,
) -> Result<(Strength, Confidence)> {
    if episodes.len() >= 3 {
        return Ok((Strength::VeryStrong, assign_confidence(ConfidencePattern::ThreeOrMore, episodes.len() as i64)));
    }

    if episodes.len() == 2 {
        return classify_exactly_two_episodes_for_group(connection, pilot_id, entity_type, entity_id, episodes, now);
    }

    let (strength, basis) = classify_recency_decay_for_group(connection, pilot_id, entity_type, entity_id, episodes[0].end_utc, now)?;
    Ok((strength, assign_confidence(ConfidencePattern::Recency, basis)))
}

pub fn classify_pilot_vs_group_strength(
    connection: &Connection,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
) -> Result<Option<(Strength, Confidence)>> {
    let pilot_timeline = fetch_pilot_affiliation_segments(connection, pilot_id)?;
    let group_timeline = [group_affiliation_segment(entity_type, entity_id)];

    if is_currently_same_c_a(&pilot_timeline, &group_timeline) {
        return Ok(None);
    }

    let episodes = build_same_c_a_episodes(&pilot_timeline, &group_timeline);

    if episodes.is_empty() {
        return Ok(None);
    }

    let now = Utc::now();
    classify_by_episode_count_for_group(connection, pilot_id, entity_type, entity_id, &episodes, now).map(Some)
}

/// Outcome of classify_pilot_vs_group_strength_with_active_entity_short_circuit:
/// distinguishes "entity is closed, evaluation skipped entirely" (Design
/// Specification Section 6.11.4, Implementation Plan Step 19.01.08) from a
/// completed classification, which may itself still be None (Section 6.11.4
/// currently-same-c/a or never-anchored cases -- see classify_pilot_vs_group_strength).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PilotVsGroupOutcome {
    Skipped,
    Classified(Option<(Strength, Confidence)>),
}

/// Active Entity Short-Circuit (Design Specification Section 6.11.4,
/// Implementation Plan Step 19.01.08): checks entity G's active status
/// (historic_closed_entity_cache first, then ESI if not already cached
/// closed -- Section 4.7) before computing/storing a Pilot-vs-Group
/// relationship for it; evaluation is skipped entirely if closed.
/// esi_active_status::ensure_corporation_active_status_cached/
/// ensure_alliance_active_status_cached are the production entry points
/// built in Step 19.01.02 for exactly this wiring -- they also perform the
/// purge-on-closure side effect (Step 19.01.08) against
/// historic_relationship_classification when a closure is newly discovered.
pub fn classify_pilot_vs_group_strength_with_active_entity_short_circuit(
    connection: &Connection,
    esi_client: &EsiActiveStatusClient,
    pilot_id: i64,
    entity_type: EntityType,
    entity_id: i64,
    now_utc: &str,
) -> Result<PilotVsGroupOutcome, String> {
    let is_active = match entity_type {
        EntityType::Corporation => ensure_corporation_active_status_cached(connection, esi_client, entity_id, now_utc),
        EntityType::Alliance => ensure_alliance_active_status_cached(connection, esi_client, entity_id, now_utc),
    }?;

    if !is_active {
        return Ok(PilotVsGroupOutcome::Skipped);
    }

    classify_pilot_vs_group_strength(connection, pilot_id, entity_type, entity_id)
        .map(PilotVsGroupOutcome::Classified)
        .map_err(|error| format!("Failed to classify pilot-vs-group strength: {error}"))
}

#[cfg(test)]
mod tests {
    use chrono::{Duration, TimeZone};

    use super::*;

    fn fixed_now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 1, 1, 0, 0, 0).unwrap()
    }

    fn days_before_fixed_now(days: i64) -> DateTime<Utc> {
        fixed_now() - Duration::days(days)
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

    fn insert_participant(connection: &Connection, evidence_id: i64, character_id: i64, corporation_id: Option<i64>, alliance_id: Option<i64>) {
        connection
            .execute(
                "INSERT INTO historic_relationship_evidence_participants \
                 (evidence_id, character_id, corporation_id, alliance_id, ship_type_id) \
                 VALUES (?, ?, ?, ?, NULL);",
                params![evidence_id, character_id, corporation_id, alliance_id],
            )
            .unwrap();
    }

    fn insert_alongside_evidence(
        connection: &Connection,
        evidence_id: i64,
        killmail_time_utc: DateTime<Utc>,
        pilot_id: i64,
        other_character_id: i64,
        other_corporation_id: Option<i64>,
        other_alliance_id: Option<i64>,
    ) {
        insert_evidence(connection, evidence_id, killmail_time_utc);
        insert_participant(connection, evidence_id, pilot_id, None, None);
        insert_participant(connection, evidence_id, other_character_id, other_corporation_id, other_alliance_id);
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

    const PILOT: i64 = 95465499;
    const OTHER_CHARACTER: i64 = 91555777;
    const GROUP_CORP: i64 = 90333333;
    const GROUP_ALLIANCE: i64 = 99005338;
    const OTHER_CORP: i64 = 90444444;

    fn ago(days: i64) -> DateTime<Utc> {
        Utc::now() - Duration::days(days)
    }

    #[test]
    fn alongside_column_selects_the_column_matching_entity_type() {
        assert_eq!(alongside_column(EntityType::Corporation), "corporation_id");
        assert_eq!(alongside_column(EntityType::Alliance), "alliance_id");
    }

    #[test]
    fn group_affiliation_segment_corporation_carries_only_the_corporation_id() {
        let segment = group_affiliation_segment(EntityType::Corporation, GROUP_CORP);
        assert_eq!(segment.corporation_id, Some(GROUP_CORP));
        assert_eq!(segment.alliance_id, None);
    }

    #[test]
    fn group_affiliation_segment_alliance_carries_only_the_alliance_id() {
        let segment = group_affiliation_segment(EntityType::Alliance, GROUP_ALLIANCE);
        assert_eq!(segment.corporation_id, None);
        assert_eq!(segment.alliance_id, Some(GROUP_ALLIANCE));
    }

    #[test]
    fn most_recent_alongside_evidence_time_ignores_evidence_where_the_other_participant_is_not_in_the_group() {
        let connection = open_test_schema();
        insert_alongside_evidence(&connection, 1, days_before_fixed_now(10), PILOT, OTHER_CHARACTER, Some(OTHER_CORP), None);

        let result = most_recent_alongside_evidence_time(&connection, PILOT, EntityType::Corporation, GROUP_CORP, days_before_fixed_now(100)).unwrap();

        assert_eq!(result, None);
    }

    #[test]
    fn most_recent_alongside_evidence_time_returns_the_latest_matching_row_after_the_threshold() {
        let connection = open_test_schema();
        insert_alongside_evidence(&connection, 1, days_before_fixed_now(100), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);
        insert_alongside_evidence(&connection, 2, days_before_fixed_now(50), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);

        let result = most_recent_alongside_evidence_time(&connection, PILOT, EntityType::Corporation, GROUP_CORP, days_before_fixed_now(150)).unwrap();

        assert_eq!(result, Some(days_before_fixed_now(50)));
    }

    #[test]
    fn has_alongside_evidence_between_true_for_an_alliance_match_strictly_inside_the_window() {
        let connection = open_test_schema();
        insert_alongside_evidence(&connection, 1, days_before_fixed_now(15), PILOT, OTHER_CHARACTER, None, Some(GROUP_ALLIANCE));

        let result = has_alongside_evidence_between(&connection, PILOT, EntityType::Alliance, GROUP_ALLIANCE, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert!(result);
    }

    #[test]
    fn count_alongside_evidence_in_window_counts_only_matching_rows_inside_the_inclusive_window() {
        let connection = open_test_schema();
        insert_alongside_evidence(&connection, 1, days_before_fixed_now(20), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);
        insert_alongside_evidence(&connection, 2, days_before_fixed_now(15), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);
        insert_alongside_evidence(&connection, 3, days_before_fixed_now(25), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);
        insert_alongside_evidence(&connection, 4, days_before_fixed_now(15), PILOT, OTHER_CHARACTER, Some(OTHER_CORP), None);

        let count = count_alongside_evidence_in_window(&connection, PILOT, EntityType::Corporation, GROUP_CORP, days_before_fixed_now(20), days_before_fixed_now(10)).unwrap();

        assert_eq!(count, 2);
    }

    #[test]
    fn classify_pilot_vs_group_strength_none_when_currently_same_c_a() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(400), ago(1));

        assert_eq!(classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(), None);
    }

    #[test]
    fn classify_pilot_vs_group_strength_none_when_entity_never_appears_in_the_pilots_own_affiliation_history() {
        let connection = open_test_schema();
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(400), ago(1));

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(),
            None,
            "no membership to anchor a row to (Design_Spec_Dense.md §6.11.4) -- must not mirror Pilot-to-Pilot's never-same-c/a Strong rule"
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_very_strong_with_three_or_more_episodes() {
        let connection = open_test_schema();

        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1060), ago(1050));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1050), ago(1040));
        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1010), ago(1));

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(),
            Some((Strength::VeryStrong, Confidence::Average))
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_strong_with_exactly_two_episodes_and_between_episode_alongside_evidence() {
        let connection = open_test_schema();

        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1010), ago(1));

        insert_alongside_evidence(&connection, 1, ago(1025), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(),
            Some((Strength::Strong, Confidence::Low))
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_collapses_two_episodes_without_between_episode_evidence_and_decays() {
        let connection = open_test_schema();

        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1040), ago(1030));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1030), ago(1020));
        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1020), ago(1010));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(1010), ago(1));

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(),
            Some((Strength::None, Confidence::Low))
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_exactly_one_episode_recency_decays_to_medium() {
        let connection = open_test_schema();

        insert_affiliation_segment(&connection, PILOT, Some(GROUP_CORP), None, ago(1000), ago(400));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(400), ago(1));

        insert_alongside_evidence(&connection, 1, ago(30), PILOT, OTHER_CHARACTER, Some(GROUP_CORP), None);

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Corporation, GROUP_CORP).unwrap(),
            Some((Strength::Medium, Confidence::Low))
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_works_for_alliance_entity_type() {
        let connection = open_test_schema();

        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), Some(GROUP_ALLIANCE), ago(1000), ago(400));
        insert_affiliation_segment(&connection, PILOT, Some(OTHER_CORP), None, ago(400), ago(1));

        insert_alongside_evidence(&connection, 1, ago(30), PILOT, OTHER_CHARACTER, None, Some(GROUP_ALLIANCE));

        assert_eq!(
            classify_pilot_vs_group_strength(&connection, PILOT, EntityType::Alliance, GROUP_ALLIANCE).unwrap(),
            Some((Strength::Medium, Confidence::Low))
        );
    }

    #[test]
    fn classify_pilot_vs_group_strength_with_active_entity_short_circuit_skips_a_cached_closed_entity_without_classifying() {
        let connection = open_test_schema();
        connection
            .execute(
                "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) VALUES (?, 'C', ?);",
                params![GROUP_CORP, "2026-08-01T00:00:00Z"],
            )
            .unwrap();
        // No affiliation data at all for PILOT -- classify_pilot_vs_group_strength
        // would itself return Ok(None) here (never anchored), so asserting
        // Skipped rather than Classified(None) proves the short circuit, not
        // the classifier, produced this outcome.
        let esi_client = EsiActiveStatusClient::new().unwrap();

        let outcome = classify_pilot_vs_group_strength_with_active_entity_short_circuit(
            &connection,
            &esi_client,
            PILOT,
            EntityType::Corporation,
            GROUP_CORP,
            "2026-08-13T00:00:00Z",
        );

        assert_eq!(outcome, Ok(PilotVsGroupOutcome::Skipped));
    }
}
