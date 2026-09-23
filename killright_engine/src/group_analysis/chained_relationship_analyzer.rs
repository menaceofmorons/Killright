use std::collections::{HashMap, HashSet};

use chrono::{DateTime, Duration, Utc};

use crate::group_analysis::direct_relationship_analyzer::DirectRelationship;
use crate::repositories::killmail_relationship_repository::KillmailAttackerEvidence;

pub const PROVISIONAL_CHAIN_WEIGHT_AT_AGE_ZERO: f64 = 50.0;

#[derive(Debug, Clone, PartialEq)]
pub struct ChainedRelationship {
    pub pilot_a: i64,
    pub pilot_c: i64,
    pub intermediary_pilot_b: i64,
    pub chain_age_days: f64,
    pub decayed_weight: f64,
}

pub fn analyze_chained_relationships(
    evidence: &[KillmailAttackerEvidence],
    direct_relationships: &[DirectRelationship],
    minimum_shared_events: i64,
    chain_weight_at_age_zero: f64,
    recent_window_days: i64,
    now: DateTime<Utc>,
) -> Vec<ChainedRelationship> {
    let attackers_by_killmail = group_attackers_by_killmail(evidence);
    let kill_times_by_pair = build_kill_times_by_pair(&attackers_by_killmail);
    let qualifying_links =
        build_qualifying_links(&kill_times_by_pair, minimum_shared_events, recent_window_days, now);
    let adjacency = build_adjacency(&qualifying_links);
    let direct_pairs = build_direct_pair_set(direct_relationships);

    let mut best_by_pair: HashMap<(i64, i64), ChainedRelationship> = HashMap::new();

    for (&intermediary, neighbors) in &adjacency {
        for i in 0..neighbors.len() {
            for j in (i + 1)..neighbors.len() {
                let (pilot_x, time_x) = neighbors[i];
                let (pilot_y, time_y) = neighbors[j];
                let pair_key = ordered_pair(pilot_x, pilot_y);

                if direct_pairs.contains(&pair_key) {
                    continue;
                }

                let older_within_window_time = time_x.min(time_y);
                let age_days = ((now - older_within_window_time).num_seconds() as f64 / 86400.0).max(0.0);
                let decayed_weight = (chain_weight_at_age_zero
                    * (1.0 - age_days / recent_window_days as f64))
                    .clamp(0.0, chain_weight_at_age_zero);

                let candidate = ChainedRelationship {
                    pilot_a: pair_key.0,
                    pilot_c: pair_key.1,
                    intermediary_pilot_b: intermediary,
                    chain_age_days: age_days,
                    decayed_weight,
                };

                best_by_pair
                    .entry(pair_key)
                    .and_modify(|existing| {
                        if candidate.decayed_weight > existing.decayed_weight {
                            *existing = candidate.clone();
                        }
                    })
                    .or_insert(candidate);
            }
        }
    }

    let mut relationships: Vec<ChainedRelationship> = best_by_pair.into_values().collect();
    relationships.sort_by(|left, right| {
        left.pilot_a
            .cmp(&right.pilot_a)
            .then(left.pilot_c.cmp(&right.pilot_c))
    });
    relationships
}

fn group_attackers_by_killmail(
    evidence: &[KillmailAttackerEvidence],
) -> HashMap<i64, Vec<&KillmailAttackerEvidence>> {
    let mut grouped: HashMap<i64, Vec<&KillmailAttackerEvidence>> = HashMap::new();

    for row in evidence {
        grouped.entry(row.killmail_id).or_default().push(row);
    }

    grouped
}

fn build_kill_times_by_pair(
    attackers_by_killmail: &HashMap<i64, Vec<&KillmailAttackerEvidence>>,
) -> HashMap<(i64, i64), Vec<DateTime<Utc>>> {
    let mut kill_times: HashMap<(i64, i64), Vec<DateTime<Utc>>> = HashMap::new();

    for attackers in attackers_by_killmail.values() {
        for i in 0..attackers.len() {
            for j in (i + 1)..attackers.len() {
                let Some(kill_time) = parse_kill_time(&attackers[i].kill_time_utc) else {
                    continue;
                };

                let pair_key = ordered_pair(attackers[i].character_id, attackers[j].character_id);
                kill_times.entry(pair_key).or_default().push(kill_time);
            }
        }
    }

    kill_times
}

fn parse_kill_time(kill_time_utc: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(kill_time_utc)
        .ok()
        .map(|timestamp| timestamp.with_timezone(&Utc))
}

fn build_qualifying_links(
    kill_times_by_pair: &HashMap<(i64, i64), Vec<DateTime<Utc>>>,
    minimum_shared_events: i64,
    recent_window_days: i64,
    now: DateTime<Utc>,
) -> HashMap<(i64, i64), DateTime<Utc>> {
    let window_cutoff = now - Duration::days(recent_window_days);
    let mut qualifying = HashMap::new();

    for (&pair_key, times) in kill_times_by_pair {
        if (times.len() as i64) < minimum_shared_events {
            continue;
        }

        let most_recent_within_window = times.iter().filter(|time| **time >= window_cutoff).max().copied();

        if let Some(most_recent_within_window) = most_recent_within_window {
            qualifying.insert(pair_key, most_recent_within_window);
        }
    }

    qualifying
}

fn build_adjacency(
    qualifying_links: &HashMap<(i64, i64), DateTime<Utc>>,
) -> HashMap<i64, Vec<(i64, DateTime<Utc>)>> {
    let mut adjacency: HashMap<i64, Vec<(i64, DateTime<Utc>)>> = HashMap::new();

    for (&(pilot_a, pilot_b), &most_recent_within_window) in qualifying_links {
        adjacency
            .entry(pilot_a)
            .or_default()
            .push((pilot_b, most_recent_within_window));
        adjacency
            .entry(pilot_b)
            .or_default()
            .push((pilot_a, most_recent_within_window));
    }

    adjacency
}

fn build_direct_pair_set(direct_relationships: &[DirectRelationship]) -> HashSet<(i64, i64)> {
    direct_relationships
        .iter()
        .map(|relationship| ordered_pair(relationship.pilot_a, relationship.pilot_b))
        .collect()
}

fn ordered_pair(character_a: i64, character_b: i64) -> (i64, i64) {
    if character_a <= character_b {
        (character_a, character_b)
    } else {
        (character_b, character_a)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn evidence(
        killmail_id: i64,
        character_id: i64,
        kill_time_utc: &str,
    ) -> KillmailAttackerEvidence {
        KillmailAttackerEvidence {
            killmail_id,
            character_id,
            corporation_id: None,
            alliance_id: None,
            kill_time_utc: kill_time_utc.to_string(),
        }
    }

    fn now() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 9, 23, 0, 0, 0).unwrap()
    }

    #[test]
    fn chain_with_both_links_inside_window_is_detected_with_decayed_weight() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, "2026-09-17T00:00:00+00:00"),
        ];

        let relationships =
            analyze_chained_relationships(&evidence_rows, &[], 2, 50.0, 14, now());

        assert_eq!(relationships.len(), 1);
        let chain = &relationships[0];
        assert_eq!((chain.pilot_a, chain.pilot_c), (100, 300));
        assert_eq!(chain.intermediary_pilot_b, 200);
        assert!((chain.chain_age_days - 6.0).abs() < 1e-9);
        assert!((chain.decayed_weight - (50.0 * (1.0 - 6.0 / 14.0))).abs() < 1e-9);
    }

    #[test]
    fn link_with_no_shared_kill_inside_window_does_not_qualify_chain() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-08-01T00:00:00+00:00"),
            evidence(1, 200, "2026-08-01T00:00:00+00:00"),
            evidence(2, 100, "2026-08-05T00:00:00+00:00"),
            evidence(2, 200, "2026-08-05T00:00:00+00:00"),
            evidence(3, 200, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, "2026-09-17T00:00:00+00:00"),
        ];

        let relationships =
            analyze_chained_relationships(&evidence_rows, &[], 2, 50.0, 14, now());

        assert!(relationships.is_empty());
    }

    #[test]
    fn link_below_minimum_shared_events_does_not_qualify_chain() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, "2026-09-20T00:00:00+00:00"),
            evidence(3, 200, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, "2026-09-17T00:00:00+00:00"),
        ];

        let relationships =
            analyze_chained_relationships(&evidence_rows, &[], 2, 50.0, 14, now());

        assert!(relationships.is_empty());
    }

    #[test]
    fn intermediary_absent_from_scan_set_produces_no_chain() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, "2026-09-21T00:00:00+00:00"),
        ];

        let relationships =
            analyze_chained_relationships(&evidence_rows, &[], 2, 50.0, 14, now());

        assert!(relationships.is_empty());
    }

    #[test]
    fn existing_direct_relationship_suppresses_chain_for_same_pair() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-09-20T00:00:00+00:00"),
            evidence(1, 200, "2026-09-20T00:00:00+00:00"),
            evidence(2, 100, "2026-09-21T00:00:00+00:00"),
            evidence(2, 200, "2026-09-21T00:00:00+00:00"),
            evidence(3, 200, "2026-09-15T00:00:00+00:00"),
            evidence(3, 300, "2026-09-15T00:00:00+00:00"),
            evidence(4, 200, "2026-09-17T00:00:00+00:00"),
            evidence(4, 300, "2026-09-17T00:00:00+00:00"),
        ];
        let direct_relationships = vec![DirectRelationship {
            pilot_a: 100,
            pilot_b: 300,
            counted_shared_kills: 2,
            last_counted_kill_time_utc: "2026-09-18T00:00:00+00:00".to_string(),
        }];

        let relationships = analyze_chained_relationships(
            &evidence_rows,
            &direct_relationships,
            2,
            50.0,
            14,
            now(),
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn multiple_intermediaries_for_same_pair_keep_the_strongest() {
        let evidence_rows = vec![
            evidence(1, 100, "2026-09-21T00:00:00+00:00"),
            evidence(1, 200, "2026-09-21T00:00:00+00:00"),
            evidence(2, 100, "2026-09-22T00:00:00+00:00"),
            evidence(2, 200, "2026-09-22T00:00:00+00:00"),
            evidence(3, 200, "2026-09-20T00:00:00+00:00"),
            evidence(3, 300, "2026-09-20T00:00:00+00:00"),
            evidence(4, 200, "2026-09-21T00:00:00+00:00"),
            evidence(4, 300, "2026-09-21T00:00:00+00:00"),
            evidence(5, 100, "2026-09-10T00:00:00+00:00"),
            evidence(5, 400, "2026-09-10T00:00:00+00:00"),
            evidence(6, 100, "2026-09-11T00:00:00+00:00"),
            evidence(6, 400, "2026-09-11T00:00:00+00:00"),
            evidence(7, 400, "2026-09-10T00:00:00+00:00"),
            evidence(7, 300, "2026-09-10T00:00:00+00:00"),
            evidence(8, 400, "2026-09-12T00:00:00+00:00"),
            evidence(8, 300, "2026-09-12T00:00:00+00:00"),
        ];

        let relationships =
            analyze_chained_relationships(&evidence_rows, &[], 2, 50.0, 14, now());

        let a_c_chains: Vec<&ChainedRelationship> = relationships
            .iter()
            .filter(|chain| (chain.pilot_a, chain.pilot_c) == (100, 300))
            .collect();

        assert_eq!(a_c_chains.len(), 1);
        assert_eq!(a_c_chains[0].intermediary_pilot_b, 200);
        assert!((a_c_chains[0].chain_age_days - 2.0).abs() < 1e-9);
    }
}
