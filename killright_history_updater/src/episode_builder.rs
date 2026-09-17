use chrono::{DateTime, Utc};
use duckdb::{params, Connection, Result};

pub const NPC_CORPORATION_ID_THRESHOLD: i64 = 1_005_000;

pub fn is_npc_corporation(corporation_id: i64) -> bool {
    corporation_id < NPC_CORPORATION_ID_THRESHOLD
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AffiliationSegment {
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub first_seen_utc: DateTime<Utc>,
    pub last_seen_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Episode {
    pub start_utc: DateTime<Utc>,
    pub end_utc: DateTime<Utc>,
}

pub fn is_same_c_a(a: &AffiliationSegment, b: &AffiliationSegment) -> bool {
    if a.corporation_id.is_some_and(is_npc_corporation) {
        return false;
    }
    if b.corporation_id.is_some_and(is_npc_corporation) {
        return false;
    }

    if a.corporation_id.is_some() && a.corporation_id == b.corporation_id {
        return true;
    }

    a.alliance_id.is_some() && a.alliance_id == b.alliance_id
}

pub fn fixed_group_segment(corporation_id: Option<i64>, alliance_id: Option<i64>) -> AffiliationSegment {
    AffiliationSegment {
        corporation_id,
        alliance_id,
        first_seen_utc: DateTime::<Utc>::MIN_UTC,
        last_seen_utc: DateTime::<Utc>::MAX_UTC,
    }
}

pub fn build_same_c_a_episodes(timeline_a: &[AffiliationSegment], timeline_b: &[AffiliationSegment]) -> Vec<Episode> {
    let mut episodes: Vec<Episode> = Vec::new();
    let mut open_episode: Option<Episode> = None;

    let mut index_a = 0usize;
    let mut index_b = 0usize;

    while index_a < timeline_a.len() && index_b < timeline_b.len() {
        let segment_a = &timeline_a[index_a];
        let segment_b = &timeline_b[index_b];

        let overlap_start = segment_a.first_seen_utc.max(segment_b.first_seen_utc);
        let overlap_end = segment_a.last_seen_utc.min(segment_b.last_seen_utc);

        if overlap_start <= overlap_end {
            if is_same_c_a(segment_a, segment_b) {
                match &open_episode {
                    Some(episode) if episode.end_utc == overlap_start => {
                        open_episode = Some(Episode {
                            start_utc: episode.start_utc,
                            end_utc: overlap_end,
                        });
                    }
                    _ => {
                        if let Some(episode) = open_episode.take() {
                            episodes.push(episode);
                        }
                        open_episode = Some(Episode {
                            start_utc: overlap_start,
                            end_utc: overlap_end,
                        });
                    }
                }
            } else if let Some(episode) = open_episode.take() {
                episodes.push(episode);
            }
        }

        if segment_a.last_seen_utc < segment_b.last_seen_utc {
            index_a += 1;
        } else if segment_b.last_seen_utc < segment_a.last_seen_utc {
            index_b += 1;
        } else {
            index_a += 1;
            index_b += 1;
        }
    }

    if let Some(episode) = open_episode.take() {
        episodes.push(episode);
    }

    episodes
}

pub fn fetch_pilot_affiliation_segments(connection: &Connection, pilot_id: i64) -> Result<Vec<AffiliationSegment>> {
    let mut statement = connection.prepare(
        "SELECT corporation_id, alliance_id, first_seen_utc, last_seen_utc \
         FROM historic_pilot_affiliation_timeline \
         WHERE pilot_id = ? \
         ORDER BY first_seen_utc ASC;",
    )?;

    let rows = statement.query_map(params![pilot_id], |row| {
        Ok((
            row.get::<_, Option<i64>>(0)?,
            row.get::<_, Option<i64>>(1)?,
            row.get::<_, String>(2)?,
            row.get::<_, String>(3)?,
        ))
    })?;

    let mut segments = Vec::new();

    for row in rows {
        let (corporation_id, alliance_id, first_seen_utc, last_seen_utc) = row?;

        let first_seen_utc = DateTime::parse_from_rfc3339(&first_seen_utc)
            .unwrap_or_else(|error| {
                panic!("historic_pilot_affiliation_timeline.first_seen_utc for pilot_id {pilot_id} is not valid RFC3339 ({first_seen_utc}): {error}")
            })
            .with_timezone(&Utc);
        let last_seen_utc = DateTime::parse_from_rfc3339(&last_seen_utc)
            .unwrap_or_else(|error| {
                panic!("historic_pilot_affiliation_timeline.last_seen_utc for pilot_id {pilot_id} is not valid RFC3339 ({last_seen_utc}): {error}")
            })
            .with_timezone(&Utc);

        segments.push(AffiliationSegment {
            corporation_id,
            alliance_id,
            first_seen_utc,
            last_seen_utc,
        });
    }

    Ok(segments)
}

#[cfg(test)]
mod tests {
    use chrono::TimeZone;

    use super::*;

    fn day(offset_days: i64) -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 1, 1, 0, 0, 0).unwrap() + chrono::Duration::days(offset_days)
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
    fn is_npc_corporation_true_below_threshold() {
        assert!(is_npc_corporation(NPC_CORPORATION_ID_THRESHOLD - 1));
    }

    #[test]
    fn is_npc_corporation_false_at_and_above_threshold() {
        assert!(!is_npc_corporation(NPC_CORPORATION_ID_THRESHOLD));
        assert!(!is_npc_corporation(98765432));
    }

    #[test]
    fn is_same_c_a_true_when_corporation_ids_match_even_with_different_alliances() {
        let a = segment(Some(98765432), Some(99005338), 0, 10);
        let b = segment(Some(98765432), Some(99009999), 0, 10);

        assert!(is_same_c_a(&a, &b));
    }

    #[test]
    fn is_same_c_a_true_when_alliance_ids_match_across_different_corporations() {
        let a = segment(Some(98765432), Some(99005338), 0, 10);
        let b = segment(Some(90379338), Some(99005338), 0, 10);

        assert!(is_same_c_a(&a, &b));
    }

    #[test]
    fn is_same_c_a_false_when_neither_corporation_nor_alliance_matches() {
        let a = segment(Some(98765432), Some(99005338), 0, 10);
        let b = segment(Some(90379338), Some(99009999), 0, 10);

        assert!(!is_same_c_a(&a, &b));
    }

    #[test]
    fn is_same_c_a_false_when_both_sides_are_unaligned() {
        let a = segment(Some(98765432), None, 0, 10);
        let b = segment(Some(90379338), None, 0, 10);

        assert!(!is_same_c_a(&a, &b), "two different, unaligned corporations must not be treated as same c/a via None == None");
    }

    #[test]
    fn is_same_c_a_false_when_corporation_ids_match_but_corporation_is_npc() {
        let npc_corp = NPC_CORPORATION_ID_THRESHOLD - 1;
        let a = segment(Some(npc_corp), None, 0, 10);
        let b = segment(Some(npc_corp), None, 0, 10);

        assert!(!is_same_c_a(&a, &b), "identical NPC corporation membership carries no relationship signal");
    }

    #[test]
    fn is_same_c_a_false_when_only_one_side_is_npc() {
        let a = segment(Some(NPC_CORPORATION_ID_THRESHOLD - 1), Some(99005338), 0, 10);
        let b = segment(Some(90379338), Some(99005338), 0, 10);

        assert!(!is_same_c_a(&a, &b));
    }

    #[test]
    fn build_same_c_a_episodes_returns_one_episode_for_a_full_overlap() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert_eq!(episodes, vec![Episode { start_utc: day(0), end_utc: day(10) }]);
    }

    #[test]
    fn build_same_c_a_episodes_returns_empty_when_never_same_c_a() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];
        let timeline_b = vec![segment(Some(90379338), None, 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert!(episodes.is_empty());
    }

    #[test]
    fn build_same_c_a_episodes_merges_across_a_corporation_change_that_stays_same_c_a() {
        let timeline_a = vec![
            segment(Some(98765432), Some(99005338), 0, 5),
            segment(Some(90379338), Some(99005338), 5, 10),
        ];
        let timeline_b = vec![segment(Some(90222222), Some(99005338), 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert_eq!(
            episodes,
            vec![Episode { start_utc: day(0), end_utc: day(10) }],
            "same c/a stayed continuously true via alliance, so the corporation change must not split the episode"
        );
    }

    #[test]
    fn build_same_c_a_episodes_splits_across_a_real_gap_in_coverage() {
        let timeline_a = vec![
            segment(Some(98765432), None, 0, 5),
            segment(Some(98765432), None, 8, 10),
        ];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert_eq!(
            episodes,
            vec![
                Episode { start_utc: day(0), end_utc: day(5) },
                Episode { start_utc: day(8), end_utc: day(10) },
            ],
            "a real gap in one side's own timeline must not be bridged -- collapsing a gap like this is Step 19.01.05's decision, not this routine's"
        );
    }

    #[test]
    fn build_same_c_a_episodes_splits_on_a_genuine_not_same_c_a_period() {
        let timeline_a = vec![
            segment(Some(98765432), None, 0, 5),
            segment(Some(90379338), None, 5, 8),
            segment(Some(98765432), None, 8, 10),
        ];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert_eq!(
            episodes,
            vec![
                Episode { start_utc: day(0), end_utc: day(5) },
                Episode { start_utc: day(8), end_utc: day(10) },
            ]
        );
    }

    #[test]
    fn build_same_c_a_episodes_returns_empty_when_either_timeline_is_empty() {
        let timeline_a = vec![segment(Some(98765432), None, 0, 10)];

        assert!(build_same_c_a_episodes(&timeline_a, &[]).is_empty());
        assert!(build_same_c_a_episodes(&[], &timeline_a).is_empty());
    }

    #[test]
    fn build_same_c_a_episodes_accepts_a_zero_width_single_instant_overlap() {
        let timeline_a = vec![segment(Some(98765432), None, 5, 5)];
        let timeline_b = vec![segment(Some(98765432), None, 0, 10)];

        let episodes = build_same_c_a_episodes(&timeline_a, &timeline_b);

        assert_eq!(episodes, vec![Episode { start_utc: day(5), end_utc: day(5) }]);
    }

    #[test]
    fn fixed_group_segment_as_corporation_matches_only_via_corporation_id() {
        let g = fixed_group_segment(Some(98765432), None);
        let timeline = vec![segment(Some(98765432), Some(99005338), 3, 7)];

        let episodes = build_same_c_a_episodes(&timeline, std::slice::from_ref(&g));

        assert_eq!(
            episodes,
            vec![Episode { start_utc: day(3), end_utc: day(7) }],
            "the pilot's own segment bounds the episode, even though G itself is unbounded"
        );
    }

    #[test]
    fn fixed_group_segment_as_alliance_matches_via_alliance_id_regardless_of_corporation() {
        let g = fixed_group_segment(None, Some(99005338));
        let timeline = vec![segment(Some(90379338), Some(99005338), 3, 7)];

        let episodes = build_same_c_a_episodes(&timeline, std::slice::from_ref(&g));

        assert_eq!(episodes, vec![Episode { start_utc: day(3), end_utc: day(7) }]);
    }

    #[test]
    fn fixed_group_segment_as_corporation_does_not_match_a_different_corporation() {
        let g = fixed_group_segment(Some(98765432), None);
        let timeline = vec![segment(Some(90379338), None, 3, 7)];

        let episodes = build_same_c_a_episodes(&timeline, std::slice::from_ref(&g));

        assert!(episodes.is_empty());
    }

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    #[test]
    fn fetch_pilot_affiliation_segments_returns_rows_in_chronological_order() {
        let connection = open_test_schema();

        connection
            .execute(
                "INSERT INTO historic_pilot_affiliation_timeline \
                 (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
                 VALUES (95465499, 90379338, NULL, ?, ?, ?);",
                params![day(5).to_rfc3339(), day(8).to_rfc3339(), day(8).to_rfc3339()],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO historic_pilot_affiliation_timeline \
                 (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
                 VALUES (95465499, 98765432, NULL, ?, ?, ?);",
                params![day(0).to_rfc3339(), day(4).to_rfc3339(), day(4).to_rfc3339()],
            )
            .unwrap();

        let segments = fetch_pilot_affiliation_segments(&connection, 95465499).unwrap();

        assert_eq!(segments.len(), 2);
        assert_eq!(segments[0].corporation_id, Some(98765432));
        assert_eq!(segments[0].first_seen_utc, day(0));
        assert_eq!(segments[1].corporation_id, Some(90379338));
        assert_eq!(segments[1].first_seen_utc, day(5));
    }

    #[test]
    fn fetch_pilot_affiliation_segments_returns_empty_for_a_pilot_with_no_rows() {
        let connection = open_test_schema();

        let segments = fetch_pilot_affiliation_segments(&connection, 95465499).unwrap();

        assert!(segments.is_empty());
    }
}
