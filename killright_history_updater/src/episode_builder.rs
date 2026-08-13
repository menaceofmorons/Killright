use chrono::{DateTime, Utc};
use duckdb::{params, Connection, Result};

/// Default `npcCorporationIdThreshold` (Design Specification Sections
/// 4.7/6.8, reused by 6.11.6): corporation IDs below this are EVE Online's
/// generic NPC corporations. An explicit placeholder, like every other
/// threshold in this component -- Tom expects this to be revised once more
/// real scenarios are worked through. Section 6.11.6 frames this as
/// "config-driven," but no config-loading mechanism exists in this crate for
/// any threshold in this family yet, so this follows the same established
/// convention every other numeric constant in this crate already uses -- a
/// plain `pub const` (see `staging.rs`'s `TEST_MODE_ANCHOR_YEAR`,
/// `DEFAULT_INITIAL_HISTORIC_IMPORT_HORIZON_YEARS`). Section 6.11.6's
/// optional `genericNpcCorporationIds` override list is not implemented --
/// out of scope until a real scenario needs it.
pub const NPC_CORPORATION_ID_THRESHOLD: i64 = 1_005_000;

/// True when `corporation_id` is a generic NPC corporation (Design
/// Specification Sections 4.7/6.8/6.11.4/6.11.6).
pub fn is_npc_corporation(corporation_id: i64) -> bool {
    corporation_id < NPC_CORPORATION_ID_THRESHOLD
}

/// One party's corporation/alliance affiliation over a closed time range
/// `[first_seen_utc, last_seen_utc]` -- either a real
/// `historic_pilot_affiliation_timeline` row (Step 19.01.03, fetched via
/// `fetch_pilot_affiliation_segments` below), or a synthetic, unbounded
/// segment representing a fixed Pilot-vs-Group counterparty G (see
/// `fixed_group_segment` below).
///
/// `corporation_id`/`alliance_id` are both `Option<i64>` so this same struct
/// serves both cases: a real timeline row always carries `Some`
/// corporation_id (a pilot always has a corporation in EVE), but an
/// alliance-only fixed G segment carries `None` for corporation_id --
/// representing that as `Option<i64>` rather than a sentinel value avoids
/// misfiring the NPC check in `is_same_c_a` below.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AffiliationSegment {
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub first_seen_utc: DateTime<Utc>,
    pub last_seen_utc: DateTime<Utc>,
}

/// A discrete period during which two parties were continuously same c/a
/// (Design Specification Section 6.11.4), as built by
/// `build_same_c_a_episodes`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Episode {
    pub start_utc: DateTime<Utc>,
    pub end_utc: DateTime<Utc>,
}

/// The Same C/A test (Design Specification Section 6.11.4, Design Notes
/// Section 2): two affiliations are "same c/a" if their corporation IDs
/// match, or -- for different corporations -- their alliance IDs match,
/// unless either side's corporation is a generic NPC corporation, in which
/// case they are never same c/a, even NPC corp against the identical NPC
/// corp. Two affiliations both unaligned (`alliance_id: None` on both
/// sides) are never treated as a match via that `None == None` -- only a
/// `Some` alliance_id shared by both sides counts.
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

/// A synthetic, unbounded `AffiliationSegment` representing a fixed
/// Pilot-vs-Group counterparty G (Design Specification Section 6.11.4's
/// Pilot-vs-Group extension) -- either G's own corporation ID (corp case,
/// `alliance_id: None`) or alliance ID (alliance case, `corporation_id:
/// None`), never both, matching the Design Notes' Match Test: a pilot
/// matches G either via their own corporation_id or via their own
/// alliance_id, never a combined check. `first_seen_utc`/`last_seen_utc`
/// span the widest range `DateTime<Utc>` can represent, so this segment
/// always overlaps every real segment passed to `build_same_c_a_episodes`
/// as the other side -- G itself never has a timeline of its own to
/// intersect against, so the real side's own segment bounds govern the
/// resulting episodes.
pub fn fixed_group_segment(corporation_id: Option<i64>, alliance_id: Option<i64>) -> AffiliationSegment {
    AffiliationSegment {
        corporation_id,
        alliance_id,
        first_seen_utc: DateTime::<Utc>::MIN_UTC,
        last_seen_utc: DateTime::<Utc>::MAX_UTC,
    }
}

/// Builds the discrete episode list (Design Specification Section 6.11.4)
/// where `timeline_a` and `timeline_b` were continuously same c/a, by
/// sweeping the pairwise overlap between two sorted, non-overlapping
/// segment lists (a classic two-sorted-interval-list intersection) and
/// merging consecutive same-c/a overlaps that directly abut into one
/// `Episode` -- even when the underlying reason for the match changes
/// partway through (for example one party's corporation changes but they
/// stay in the same alliance throughout, so same c/a never actually lapsed).
///
/// A gap between two same-c/a overlaps -- whether from an actual
/// not-same-c/a period, or simply a stretch neither party's evidence covers
/// -- always ends an episode. Deciding whether such a gap should later be
/// treated as a genuine split or collapsed for lack of between-episode
/// evidence (Section 6.11.4's exactly-two-episodes rule) is Step
/// 19.01.05/19.01.07's job, not this routine's -- this only reports what
/// the affiliation timelines themselves show.
///
/// Both slices must already be in chronological, non-overlapping order --
/// true of every `historic_pilot_affiliation_timeline` row set returned by
/// `fetch_pilot_affiliation_segments` below (`ORDER BY first_seen_utc`), and
/// of a single-element `fixed_group_segment` slice.
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

/// Fetches a pilot's full `historic_pilot_affiliation_timeline` history
/// (Design Specification Section 4.7, Step 19.01.03), ordered
/// chronologically -- the Same C/A Test + Episode Builder's real-data input
/// (Section 6.11.3 Interfaces), for a Pilot-to-Pilot caller (Step 19.01.05)
/// to pass as either `timeline_a` or `timeline_b` to `build_same_c_a_episodes`
/// above. A Pilot-vs-Group caller (Step 19.01.07) uses this for the one real
/// pilot side only -- the fixed group counterparty is `fixed_group_segment`
/// above, not a database read.
///
/// Stored `first_seen_utc`/`last_seen_utc` values are this crate's own
/// RFC3339 output (`persistence`/`affiliation_timeline`'s own
/// `to_rfc3339()` writes) and are expected to always parse; a parse failure
/// indicates database corruption and panics with the offending pilot_id and
/// text, rather than silently defaulting or threading a non-`duckdb::Error`
/// type through this function's `Result`, matching how a `DateTime`-parse
/// precedent already exists in `r2_client.rs` for a different (external,
/// so-fallible) source.
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
