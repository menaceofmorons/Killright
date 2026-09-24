use std::collections::{HashMap, HashSet};
use std::time::Instant;

use chrono::{DateTime, Utc};

use crate::group_analysis::chained_relationship_analyzer::{analyze_chained_relationships, ChainedRelationship};
use crate::group_analysis::group_detection_configuration::GroupDetectionConfiguration;
use crate::group_analysis::group_relationship_scoring::{
    chain_confidence, chain_strength, direct_confidence, direct_strength, intermediary_bonus, round_to_integer,
    sample_factor as compute_sample_factor,
};
use crate::group_analysis::shared_relationship_evidence::{
    apply_after_split_rule, build_current_identity_index, build_shared_events_by_pair,
    group_attackers_by_killmail, is_currently_same_corporation_or_alliance, SharedKillEvent,
};
use crate::repositories::killmail_relationship_repository::KillmailAttackerEvidence;
use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AfterSplitBranch {
    AllCountedSinceSplit,
    NotSameOnly,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ContributingKillmail {
    pub killmail_id: i64,
    pub kill_time_utc: String,
    pub is_same_corporation_or_alliance: bool,
    pub unique_attacker_count: i64,
}

impl From<&SharedKillEvent> for ContributingKillmail {
    fn from(event: &SharedKillEvent) -> Self {
        Self {
            killmail_id: event.killmail_id,
            kill_time_utc: event.kill_time_utc.clone(),
            is_same_corporation_or_alliance: event.is_same_corporation_or_alliance,
            unique_attacker_count: event.unique_attacker_count,
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct DirectRelationshipDiagnostic {
    pub pilot_a: i64,
    pub pilot_b: i64,
    pub gated_by_current_membership: bool,
    pub after_split_branch: AfterSplitBranch,
    pub contributing_killmails: Vec<ContributingKillmail>,
    pub counted_shared_kills: i64,
    pub split_bonus_applied: bool,
    pub qualifies: bool,
    pub last_counted_kill_time_utc: Option<String>,
    pub gang_quality: Option<f64>,
    pub sample_factor: Option<f64>,
    pub strength: Option<i32>,
    pub confidence: Option<i32>,
}

pub fn diagnose_direct_relationships(
    evidence: &[KillmailAttackerEvidence],
    current_identities: &[PilotIdentitySnapshot],
    configuration: &GroupDetectionConfiguration,
) -> Vec<DirectRelationshipDiagnostic> {
    let current_identity_by_character = build_current_identity_index(current_identities);
    let attackers_by_killmail = group_attackers_by_killmail(evidence);
    let shared_events_by_pair =
        build_shared_events_by_pair(&attackers_by_killmail, configuration.npc_corporation_id_threshold);

    let mut diagnostics: Vec<DirectRelationshipDiagnostic> = shared_events_by_pair
        .into_iter()
        .map(|((pilot_a, pilot_b), mut events)| {
            events.sort_by(|left, right| left.kill_time_utc.cmp(&right.kill_time_utc));

            let contributing_killmails = events.iter().map(ContributingKillmail::from).collect::<Vec<_>>();

            let gated_by_current_membership = is_currently_same_corporation_or_alliance(
                &current_identity_by_character,
                pilot_a,
                pilot_b,
                configuration.npc_corporation_id_threshold,
            );

            let (counted_events, split_bonus_applied) = apply_after_split_rule(&events);
            let after_split_branch = if split_bonus_applied {
                AfterSplitBranch::AllCountedSinceSplit
            } else {
                AfterSplitBranch::NotSameOnly
            };

            let counted_shared_kills = counted_events.len() as i64;
            let qualifies =
                !gated_by_current_membership && counted_shared_kills >= configuration.minimum_shared_events;

            let last_counted_kill_time_utc = counted_events.iter().map(|event| event.kill_time_utc.clone()).max();

            let (gang_quality, sample_factor, strength, confidence) = if qualifies {
                let gang_sizes: Vec<i64> =
                    counted_events.iter().map(|event| event.unique_attacker_count).collect();

                let gang_quality =
                    100.0 * crate::group_analysis::group_relationship_scoring::average_gang_size_weight(
                        &gang_sizes,
                        &configuration.gang_size_weights,
                    );
                let sample_factor = compute_sample_factor(
                    counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration.sample_factor.minimum,
                    configuration.sample_factor.maximum,
                    configuration.sample_factor.saturates_at_counted_shared_kills,
                );
                let strength = round_to_integer(direct_strength(
                    counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration.strength_step,
                ));
                let confidence = round_to_integer(direct_confidence(
                    &gang_sizes,
                    split_bonus_applied,
                    counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration,
                ));

                (Some(gang_quality), Some(sample_factor), Some(strength), Some(confidence))
            } else {
                (None, None, None, None)
            };

            DirectRelationshipDiagnostic {
                pilot_a,
                pilot_b,
                gated_by_current_membership,
                after_split_branch,
                contributing_killmails,
                counted_shared_kills,
                split_bonus_applied,
                qualifies,
                last_counted_kill_time_utc,
                gang_quality,
                sample_factor,
                strength,
                confidence,
            }
        })
        .collect();

    diagnostics.sort_by(|left, right| left.pilot_a.cmp(&right.pilot_a).then(left.pilot_b.cmp(&right.pilot_b)));
    diagnostics
}

#[derive(Debug, Clone, PartialEq)]
pub struct ChainedRelationshipDiagnostic {
    pub pilot_a: i64,
    pub pilot_c: i64,
    pub intermediary_pilot_b: i64,
    pub intermediary_in_scan: bool,
    pub link_ab_counted_shared_kills: i64,
    pub link_ab_most_recent_in_window_kill_time_utc: String,
    pub link_ab_split_bonus_applied: bool,
    pub link_ab_strength: i32,
    pub link_ab_confidence: i32,
    pub link_cb_counted_shared_kills: i64,
    pub link_cb_most_recent_in_window_kill_time_utc: String,
    pub link_cb_split_bonus_applied: bool,
    pub link_cb_strength: i32,
    pub link_cb_confidence: i32,
    pub chain_age_days: f64,
    pub chain_strength_for_this_intermediary: i32,
    pub is_strongest_intermediary_for_pair: bool,
    pub pair_distinct_intermediary_count: i64,
    pub pair_intermediary_bonus: i32,
    pub pair_chain_confidence: i32,
}

struct ScoredCandidate<'a> {
    chain: &'a ChainedRelationship,
    link_ab_strength: i32,
    link_ab_confidence: i32,
    link_cb_strength: i32,
    link_cb_confidence: i32,
    weaker_confidence: i32,
    chain_strength_for_this_intermediary: i32,
}

pub fn diagnose_chained_relationships(
    evidence: &[KillmailAttackerEvidence],
    current_identities: &[PilotIdentitySnapshot],
    configuration: &GroupDetectionConfiguration,
    recent_window_days: i64,
    now: DateTime<Utc>,
) -> Vec<ChainedRelationshipDiagnostic> {
    let base_chains = analyze_chained_relationships(
        evidence,
        current_identities,
        configuration.npc_corporation_id_threshold,
        configuration.minimum_shared_events,
        recent_window_days,
        now,
    );

    let mut by_pair: HashMap<(i64, i64), Vec<&ChainedRelationship>> = HashMap::new();
    for chain in &base_chains {
        by_pair.entry((chain.pilot_a, chain.pilot_c)).or_default().push(chain);
    }

    let mut diagnostics = Vec::new();

    for ((pilot_a, pilot_c), candidates) in &by_pair {
        let distinct_intermediary_count = candidates.len() as i64;
        let pair_intermediary_bonus = round_to_integer(intermediary_bonus(
            distinct_intermediary_count,
            configuration.intermediary_bonus.per_additional,
            configuration.intermediary_bonus.maximum,
        ));

        let scored: Vec<ScoredCandidate> = candidates
            .iter()
            .map(|chain| {
                let link_ab_strength = round_to_integer(direct_strength(
                    chain.link_ab_counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration.strength_step,
                ));
                let link_ab_confidence = round_to_integer(direct_confidence(
                    &chain.link_ab_counted_shared_kill_gang_sizes,
                    chain.link_ab_split_bonus_applied,
                    chain.link_ab_counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration,
                ));
                let link_cb_strength = round_to_integer(direct_strength(
                    chain.link_cb_counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration.strength_step,
                ));
                let link_cb_confidence = round_to_integer(direct_confidence(
                    &chain.link_cb_counted_shared_kill_gang_sizes,
                    chain.link_cb_split_bonus_applied,
                    chain.link_cb_counted_shared_kills,
                    configuration.minimum_shared_events,
                    configuration,
                ));

                let weaker_strength = link_ab_strength.min(link_cb_strength);
                let weaker_confidence = link_ab_confidence.min(link_cb_confidence);

                let chain_strength_for_this_intermediary = round_to_integer(chain_strength(
                    weaker_strength as f64,
                    configuration.chain_discount,
                    chain.chain_age_days,
                    recent_window_days,
                ));

                ScoredCandidate {
                    chain,
                    link_ab_strength,
                    link_ab_confidence,
                    link_cb_strength,
                    link_cb_confidence,
                    weaker_confidence,
                    chain_strength_for_this_intermediary,
                }
            })
            .collect();

        let strongest = scored
            .iter()
            .map(|candidate| candidate.chain_strength_for_this_intermediary)
            .max()
            .unwrap_or(0);

        for candidate in scored {
            let pair_chain_confidence = round_to_integer(chain_confidence(
                candidate.weaker_confidence as f64,
                distinct_intermediary_count,
                configuration.intermediary_bonus.per_additional,
                configuration.intermediary_bonus.maximum,
            ));

            diagnostics.push(ChainedRelationshipDiagnostic {
                pilot_a: *pilot_a,
                pilot_c: *pilot_c,
                intermediary_pilot_b: candidate.chain.intermediary_pilot_b,
                intermediary_in_scan: candidate.chain.intermediary_in_scan,
                link_ab_counted_shared_kills: candidate.chain.link_ab_counted_shared_kills,
                link_ab_most_recent_in_window_kill_time_utc: candidate
                    .chain
                    .link_ab_most_recent_in_window_kill_time_utc
                    .clone(),
                link_ab_split_bonus_applied: candidate.chain.link_ab_split_bonus_applied,
                link_ab_strength: candidate.link_ab_strength,
                link_ab_confidence: candidate.link_ab_confidence,
                link_cb_counted_shared_kills: candidate.chain.link_cb_counted_shared_kills,
                link_cb_most_recent_in_window_kill_time_utc: candidate
                    .chain
                    .link_cb_most_recent_in_window_kill_time_utc
                    .clone(),
                link_cb_split_bonus_applied: candidate.chain.link_cb_split_bonus_applied,
                link_cb_strength: candidate.link_cb_strength,
                link_cb_confidence: candidate.link_cb_confidence,
                chain_age_days: candidate.chain.chain_age_days,
                chain_strength_for_this_intermediary: candidate.chain_strength_for_this_intermediary,
                is_strongest_intermediary_for_pair: candidate.chain_strength_for_this_intermediary == strongest,
                pair_distinct_intermediary_count: distinct_intermediary_count,
                pair_intermediary_bonus,
                pair_chain_confidence,
            });
        }
    }

    diagnostics.sort_by(|left, right| {
        left.pilot_a
            .cmp(&right.pilot_a)
            .then(left.pilot_c.cmp(&right.pilot_c))
            .then(left.intermediary_pilot_b.cmp(&right.intermediary_pilot_b))
    });
    diagnostics
}

#[derive(Debug, Clone, PartialEq)]
pub struct IntermediaryHubSummary {
    pub intermediary_pilot_id: i64,
    pub intermediary_in_scan: bool,
    pub scanned_pilots_linked_count: i64,
}

pub fn summarize_intermediary_hubs(chains: &[ChainedRelationshipDiagnostic]) -> Vec<IntermediaryHubSummary> {
    let mut linked_by_intermediary: HashMap<i64, (bool, HashSet<i64>)> = HashMap::new();

    for chain in chains {
        let entry = linked_by_intermediary
            .entry(chain.intermediary_pilot_b)
            .or_insert_with(|| (chain.intermediary_in_scan, HashSet::new()));
        entry.1.insert(chain.pilot_a);
        entry.1.insert(chain.pilot_c);
    }

    let mut hubs: Vec<IntermediaryHubSummary> = linked_by_intermediary
        .into_iter()
        .map(|(intermediary_pilot_id, (intermediary_in_scan, linked))| IntermediaryHubSummary {
            intermediary_pilot_id,
            intermediary_in_scan,
            scanned_pilots_linked_count: linked.len() as i64,
        })
        .collect();

    hubs.sort_by(|left, right| right.scanned_pilots_linked_count.cmp(&left.scanned_pilots_linked_count));
    hubs
}

#[derive(Debug, Clone, PartialEq)]
pub struct GroupDetectionDiagnostics {
    pub direct_relationships: Vec<DirectRelationshipDiagnostic>,
    pub chains: Vec<ChainedRelationshipDiagnostic>,
    pub hubs: Vec<IntermediaryHubSummary>,
    pub direct_analysis_duration_ms: i64,
    pub chain_analysis_duration_ms: i64,
}

pub fn run_group_detection_diagnostics(
    direct_evidence: &[KillmailAttackerEvidence],
    chain_evidence: &[KillmailAttackerEvidence],
    current_identities: &[PilotIdentitySnapshot],
    configuration: &GroupDetectionConfiguration,
    recent_window_days: i64,
    now: DateTime<Utc>,
) -> GroupDetectionDiagnostics {
    let direct_started_at = Instant::now();
    let direct_relationships = diagnose_direct_relationships(direct_evidence, current_identities, configuration);
    let direct_analysis_duration_ms = direct_started_at.elapsed().as_millis() as i64;

    let chain_started_at = Instant::now();
    let chains =
        diagnose_chained_relationships(chain_evidence, current_identities, configuration, recent_window_days, now);
    let chain_analysis_duration_ms = chain_started_at.elapsed().as_millis() as i64;

    let hubs = summarize_intermediary_hubs(&chains);

    GroupDetectionDiagnostics {
        direct_relationships,
        chains,
        hubs,
        direct_analysis_duration_ms,
        chain_analysis_duration_ms,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::group_analysis::group_detection_configuration::{
        GangSizeWeight, IntermediaryBonusConfiguration, SampleFactorConfiguration,
    };
    use chrono::TimeZone;

    const NPC_THRESHOLD: i64 = 1_005_000;
    const MINIMUM_SHARED_EVENTS: i64 = 2;
    const GATE_CORP: i64 = 5_000_005;
    const DEFAULT_GANG_SIZE: i64 = 2;

    fn configuration() -> GroupDetectionConfiguration {
        GroupDetectionConfiguration {
            minimum_shared_events: MINIMUM_SHARED_EVENTS,
            npc_corporation_id_threshold: NPC_THRESHOLD,
            generic_npc_corporation_ids: None,
            strength_step: 10,
            gang_size_weights: vec![
                GangSizeWeight { maximum_gang_size: 3, weight: 1.0 },
                GangSizeWeight { maximum_gang_size: 5, weight: 0.75 },
                GangSizeWeight { maximum_gang_size: 7, weight: 0.5 },
                GangSizeWeight { maximum_gang_size: 10, weight: 0.25 },
            ],
            sample_factor: SampleFactorConfiguration { minimum: 0.5, maximum: 1.0, saturates_at_counted_shared_kills: 10 },
            split_bonus: 20,
            chain_discount: 0.75,
            intermediary_bonus: IntermediaryBonusConfiguration { per_additional: 10, maximum: 30 },
        }
    }

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
    fn gated_pair_reports_gate_outcome_and_does_not_qualify() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), "2026-09-10T00:00:00+00:00"),
            evidence(2, 100, Some(1), "2026-09-12T00:00:00+00:00"),
            evidence(2, 200, Some(2), "2026-09-12T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(GATE_CORP)), identity(200, Some(GATE_CORP))];

        let diagnostics = diagnose_direct_relationships(&evidence_rows, &identities, &configuration());

        assert_eq!(diagnostics.len(), 1);
        assert!(diagnostics[0].gated_by_current_membership);
        assert!(!diagnostics[0].qualifies);
        assert!(diagnostics[0].strength.is_none());
        assert_eq!(diagnostics[0].contributing_killmails.len(), 2);
    }

    #[test]
    fn below_threshold_pair_reports_not_qualifying() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), "2026-09-10T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1)), identity(200, Some(2))];

        let diagnostics = diagnose_direct_relationships(&evidence_rows, &identities, &configuration());

        assert_eq!(diagnostics.len(), 1);
        assert!(!diagnostics[0].gated_by_current_membership);
        assert!(!diagnostics[0].qualifies);
        assert_eq!(diagnostics[0].counted_shared_kills, 1);
    }

    #[test]
    fn qualifying_pair_reports_strength_and_confidence_components() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), "2026-09-10T00:00:00+00:00"),
            evidence(2, 100, Some(1), "2026-09-12T00:00:00+00:00"),
            evidence(2, 200, Some(2), "2026-09-12T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1)), identity(200, Some(2))];

        let diagnostics = diagnose_direct_relationships(&evidence_rows, &identities, &configuration());

        assert_eq!(diagnostics.len(), 1);
        let relationship = &diagnostics[0];
        assert!(relationship.qualifies);
        assert_eq!(relationship.strength, Some(10));
        assert_eq!(relationship.gang_quality, Some(100.0));
        assert_eq!(relationship.contributing_killmails[0].killmail_id, 1);
    }

    #[test]
    fn single_intermediary_chain_reports_no_hub_bonus_and_wins_the_pair() {
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

        let diagnostics = diagnose_chained_relationships(&evidence_rows, &identities, &configuration(), 14, now());

        assert_eq!(diagnostics.len(), 1);
        assert!(diagnostics[0].is_strongest_intermediary_for_pair);
        assert_eq!(diagnostics[0].pair_distinct_intermediary_count, 1);
        assert_eq!(diagnostics[0].pair_intermediary_bonus, 0);

        let hubs = summarize_intermediary_hubs(&diagnostics);
        assert_eq!(hubs.len(), 1);
        assert_eq!(hubs[0].intermediary_pilot_id, 200);
        assert_eq!(hubs[0].scanned_pilots_linked_count, 2);
    }
}
