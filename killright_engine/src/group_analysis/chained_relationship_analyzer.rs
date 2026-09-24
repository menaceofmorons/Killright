use std::collections::{HashMap, HashSet};

use chrono::{DateTime, Duration, Utc};

use crate::group_analysis::shared_relationship_evidence::{
    apply_after_split_rule, build_current_identity_index, build_shared_events_by_pair,
    group_attackers_by_killmail, is_currently_same_corporation_or_alliance, ordered_pair, SharedKillEvent,
};
use crate::repositories::killmail_relationship_repository::KillmailAttackerEvidence;
use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;

#[derive(Debug, Clone, PartialEq)]
pub struct ChainedRelationship {
    pub pilot_a: i64,
    pub pilot_c: i64,
    pub intermediary_pilot_b: i64,
    pub intermediary_in_scan: bool,
    pub link_ab_counted_shared_kills: i64,
    pub link_ab_most_recent_in_window_kill_time_utc: String,
    pub link_ab_split_bonus_applied: bool,
    pub link_ab_counted_shared_kill_gang_sizes: Vec<i64>,
    pub link_cb_counted_shared_kills: i64,
    pub link_cb_most_recent_in_window_kill_time_utc: String,
    pub link_cb_split_bonus_applied: bool,
    pub link_cb_counted_shared_kill_gang_sizes: Vec<i64>,
    pub chain_age_days: f64,
}

struct LinkEvidence {
    counted_shared_kills: i64,
    most_recent_within_window: DateTime<Utc>,
    most_recent_within_window_kill_time_utc: String,
    split_bonus_applied: bool,
    counted_shared_kill_gang_sizes: Vec<i64>,
}

pub fn analyze_chained_relationships(
    evidence: &[KillmailAttackerEvidence],
    current_identities: &[PilotIdentitySnapshot],
    npc_corporation_id_threshold: i64,
    minimum_shared_events: i64,
    recent_window_days: i64,
    now: DateTime<Utc>,
) -> Vec<ChainedRelationship> {
    let current_identity_by_character = build_current_identity_index(current_identities);
    let scanned_character_ids: HashSet<i64> = current_identity_by_character.keys().copied().collect();
    let attackers_by_killmail = group_attackers_by_killmail(evidence);
    let shared_events_by_pair = build_shared_events_by_pair(&attackers_by_killmail, npc_corporation_id_threshold);
    let window_cutoff = now - Duration::days(recent_window_days);

    let candidates = find_candidate_intermediaries(&attackers_by_killmail, &scanned_character_ids, window_cutoff);

    let mut relationships = Vec::new();

    for (&intermediary_pilot_b, scanned_neighbors) in &candidates {
        let neighbors: Vec<i64> = scanned_neighbors.iter().copied().collect();

        for i in 0..neighbors.len() {
            for j in (i + 1)..neighbors.len() {
                let pair_key = ordered_pair(neighbors[i], neighbors[j]);

                if is_currently_same_corporation_or_alliance(
                    &current_identity_by_character,
                    pair_key.0,
                    pair_key.1,
                    npc_corporation_id_threshold,
                ) {
                    continue;
                }

                let Some(link_ab) = evaluate_link(
                    &shared_events_by_pair,
                    pair_key.0,
                    intermediary_pilot_b,
                    minimum_shared_events,
                    window_cutoff,
                ) else {
                    continue;
                };

                let Some(link_cb) = evaluate_link(
                    &shared_events_by_pair,
                    pair_key.1,
                    intermediary_pilot_b,
                    minimum_shared_events,
                    window_cutoff,
                ) else {
                    continue;
                };

                let older_within_window_time = link_ab
                    .most_recent_within_window
                    .min(link_cb.most_recent_within_window);
                let chain_age_days = ((now - older_within_window_time).num_seconds() as f64 / 86400.0).max(0.0);

                relationships.push(ChainedRelationship {
                    pilot_a: pair_key.0,
                    pilot_c: pair_key.1,
                    intermediary_pilot_b,
                    intermediary_in_scan: scanned_character_ids.contains(&intermediary_pilot_b),
                    link_ab_counted_shared_kills: link_ab.counted_shared_kills,
                    link_ab_most_recent_in_window_kill_time_utc: link_ab.most_recent_within_window_kill_time_utc,
                    link_ab_split_bonus_applied: link_ab.split_bonus_applied,
                    link_ab_counted_shared_kill_gang_sizes: link_ab.counted_shared_kill_gang_sizes,
                    link_cb_counted_shared_kills: link_cb.counted_shared_kills,
                    link_cb_most_recent_in_window_kill_time_utc: link_cb.most_recent_within_window_kill_time_utc,
                    link_cb_split_bonus_applied: link_cb.split_bonus_applied,
                    link_cb_counted_shared_kill_gang_sizes: link_cb.counted_shared_kill_gang_sizes,
                    chain_age_days,
                });
            }
        }
    }

    relationships.sort_by(|left, right| {
        left.pilot_a
            .cmp(&right.pilot_a)
            .then(left.pilot_c.cmp(&right.pilot_c))
            .then(left.intermediary_pilot_b.cmp(&right.intermediary_pilot_b))
    });
    relationships
}

fn find_candidate_intermediaries(
    attackers_by_killmail: &HashMap<i64, Vec<&KillmailAttackerEvidence>>,
    scanned_character_ids: &HashSet<i64>,
    window_cutoff: DateTime<Utc>,
) -> HashMap<i64, HashSet<i64>> {
    let mut candidates: HashMap<i64, HashSet<i64>> = HashMap::new();

    for attackers in attackers_by_killmail.values() {
        let Some(kill_time) = attackers.first().and_then(|row| parse_kill_time(&row.kill_time_utc)) else {
            continue;
        };

        if kill_time < window_cutoff {
            continue;
        }

        let scanned_on_kill: Vec<i64> = attackers
            .iter()
            .map(|row| row.character_id)
            .filter(|character_id| scanned_character_ids.contains(character_id))
            .collect();

        if scanned_on_kill.is_empty() {
            continue;
        }

        for attacker in attackers {
            for &scanned_pilot in &scanned_on_kill {
                if attacker.character_id == scanned_pilot {
                    continue;
                }

                candidates.entry(attacker.character_id).or_default().insert(scanned_pilot);
            }
        }
    }

    candidates.retain(|_, scanned_neighbors| scanned_neighbors.len() >= 2);
    candidates
}

fn evaluate_link(
    shared_events_by_pair: &HashMap<(i64, i64), Vec<SharedKillEvent>>,
    pilot_end: i64,
    intermediary_pilot_b: i64,
    minimum_shared_events: i64,
    window_cutoff: DateTime<Utc>,
) -> Option<LinkEvidence> {
    let pair_key = ordered_pair(pilot_end, intermediary_pilot_b);
    let mut events = shared_events_by_pair.get(&pair_key)?.clone();
    events.sort_by(|left, right| left.kill_time_utc.cmp(&right.kill_time_utc));

    let (counted_events, split_bonus_applied) = apply_after_split_rule(&events);

    if (counted_events.len() as i64) < minimum_shared_events {
        return None;
    }

    let (most_recent_within_window, most_recent_within_window_kill_time_utc) = counted_events
        .iter()
        .filter_map(|event| parse_kill_time(&event.kill_time_utc).map(|time| (time, event.kill_time_utc.clone())))
        .filter(|(time, _)| *time >= window_cutoff)
        .max_by_key(|(time, _)| *time)?;

    let counted_shared_kill_gang_sizes = counted_events
        .iter()
        .map(|event| event.unique_attacker_count)
        .collect();

    Some(LinkEvidence {
        counted_shared_kills: counted_events.len() as i64,
        most_recent_within_window,
        most_recent_within_window_kill_time_utc,
        split_bonus_applied,
        counted_shared_kill_gang_sizes,
    })
}

fn parse_kill_time(kill_time_utc: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(kill_time_utc)
        .ok()
        .map(|timestamp| timestamp.with_timezone(&Utc))
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    const NPC_THRESHOLD: i64 = 1_005_000;
    const MINIMUM_SHARED_EVENTS: i64 = 2;
    const RECENT_WINDOW_DAYS: i64 = 14;
    const SHARED_CORP: i64 = 5_000_009;
    const GATE_CORP: i64 = 5_000_005;
    const OTHER_CORP: i64 = 5_000_007;

    const DEFAULT_GANG_SIZE: i64 = 2;

    fn evidence(
        killmail_id: i64,
        character_id: i64,
        corporation_id: Option<i64>,
        kill_time_utc: &str,
    ) -> KillmailAttackerEvidence {
        KillmailAttackerEvidence {
            killmail_id,
            character_id,
            corporation_id,
            alliance_id: None,
            kill_time_utc: kill_time_utc.to_string(),
            unique_attacker_count: DEFAULT_GANG_SIZE,
        }
    }

    fn identity(character_id: i64, corporation_id: Option<i64>) -> PilotIdentitySnapshot {
        PilotIdentitySnapshot {
            input_name: character_id.to_string(),
            character_id: Some(character_id),
            character_name: Some(character_id.to_string()),
            verify_status: "Verified".to_string(),
            security_status: None,
            corporation_id,
            corporation_name: None,
            corporation_ticker: None,
            alliance_id: None,
            alliance_name: None,
            alliance_ticker: None,
            cached_at_utc: "2026-09-20T00:00:00+00:00".to_string(),
        }
    }

    fn now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 9, 23, 0, 0, 0).unwrap()
    }

    #[test]
    fn intermediary_outside_scan_set_produces_chain() {
        let evidence_rows = vec![
            evidence(1, 100, None, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, None, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, None, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, None, "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, None), identity(300, None)];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        assert_eq!(relationships.len(), 1);
        let chain = &relationships[0];
        assert_eq!((chain.pilot_a, chain.pilot_c), (100, 300));
        assert_eq!(chain.intermediary_pilot_b, 200);
        assert!(!chain.intermediary_in_scan);
        assert_eq!(chain.link_ab_counted_shared_kills, 2);
        assert_eq!(chain.link_ab_most_recent_in_window_kill_time_utc, "2026-09-21T00:00:00+00:00");
        assert_eq!(chain.link_cb_counted_shared_kills, 2);
        assert_eq!(chain.link_cb_most_recent_in_window_kill_time_utc, "2026-09-17T00:00:00+00:00");
        assert!((chain.chain_age_days - 6.0).abs() < 1e-9);
    }

    #[test]
    fn link_with_no_shared_kill_inside_window_does_not_qualify_chain() {
        let evidence_rows = vec![
            evidence(1, 100, None, "2026-08-01T00:00:00+00:00"),
            evidence(1, 200, None, "2026-08-01T00:00:00+00:00"),
            evidence(2, 100, None, "2026-08-05T00:00:00+00:00"),
            evidence(2, 200, None, "2026-08-05T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, None), identity(300, None)];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn link_below_minimum_shared_events_does_not_qualify_chain() {
        let evidence_rows = vec![
            evidence(1, 100, None, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, None, "2026-09-20T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, None), identity(300, None)];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn corporation_mate_intermediary_produces_no_chain() {
        let evidence_rows = vec![
            evidence(1, 100, Some(SHARED_CORP), "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, Some(SHARED_CORP), "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, Some(SHARED_CORP), "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, Some(SHARED_CORP), "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, None), identity(300, None)];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn current_membership_gate_blocks_reported_pair_but_not_links() {
        let evidence_rows = vec![
            evidence(1, 100, None, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, None, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, None, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, None, "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
            evidence(5, 200, None, "2026-09-16T00:00:00+00:00"),
            evidence(5, 400, None, "2026-09-16T00:00:00+00:00"),
            evidence(6, 200, None, "2026-09-18T00:00:00+00:00"),
            evidence(6, 400, None, "2026-09-18T00:00:00+00:00"),
        ];
        let identities = vec![
            identity(100, Some(GATE_CORP)),
            identity(300, Some(GATE_CORP)),
            identity(400, Some(OTHER_CORP)),
        ];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        assert_eq!(relationships.len(), 2);
        assert!(!relationships
            .iter()
            .any(|chain| (chain.pilot_a, chain.pilot_c) == (100, 300)));

        let pair_100_400 = relationships
            .iter()
            .find(|chain| (chain.pilot_a, chain.pilot_c) == (100, 400))
            .expect("100-400 chain via 200 is not gated");
        assert!((pair_100_400.chain_age_days - 5.0).abs() < 1e-9);

        let pair_300_400 = relationships
            .iter()
            .find(|chain| (chain.pilot_a, chain.pilot_c) == (300, 400))
            .expect("300-400 chain via 200 is not gated");
        assert!((pair_300_400.chain_age_days - 6.0).abs() < 1e-9);
    }

    #[test]
    fn all_valid_intermediaries_for_same_pair_are_retained() {
        let evidence_rows = vec![
            evidence(1, 100, None, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, None, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, None, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, None, "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, None, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, None, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, None, "2026-09-17T00:00:00+00:00"),
            evidence(5, 100, None, "2026-09-19T00:00:00+00:00"),
            evidence(5, 500, None, "2026-09-19T00:00:00+00:00"),
            evidence(6, 100, None, "2026-09-22T00:00:00+00:00"),
            evidence(6, 500, None, "2026-09-22T00:00:00+00:00"),
            evidence(7, 500, None, "2026-09-16T00:00:00+00:00"),
            evidence(7, 300, None, "2026-09-16T00:00:00+00:00"),
            evidence(8, 500, None, "2026-09-18T00:00:00+00:00"),
            evidence(8, 300, None, "2026-09-18T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, None), identity(300, None)];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
            RECENT_WINDOW_DAYS,
            now(),
        );

        let pair_chains: Vec<&ChainedRelationship> = relationships
            .iter()
            .filter(|chain| (chain.pilot_a, chain.pilot_c) == (100, 300))
            .collect();

        assert_eq!(pair_chains.len(), 2);
        let intermediaries: HashSet<i64> = pair_chains.iter().map(|chain| chain.intermediary_pilot_b).collect();
        assert_eq!(intermediaries, HashSet::from([200, 500]));
    }
}
