use std::collections::{HashMap, HashSet};

use crate::group_analysis::chained_relationship_analyzer::ChainedRelationship;
use crate::group_analysis::direct_relationship_analyzer::DirectRelationship;
use crate::group_analysis::group_detection_configuration::{GangSizeWeight, GroupDetectionConfiguration};
use crate::group_analysis::shared_relationship_evidence::ordered_pair;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RelationshipLinkType {
    Direct,
    Chain,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ScoredRelationship {
    pub pilot_a: i64,
    pub pilot_b: i64,
    pub link_type: RelationshipLinkType,
    pub strength: i32,
    pub confidence: i32,
    pub total_shared_kills: Option<i64>,
    pub last_shared_kill_time_utc: Option<String>,
    pub intermediaries_in_scan: Option<Vec<i64>>,
}

struct LinkScore {
    strength: f64,
    confidence: f64,
}

pub fn score_and_select_relationships(
    direct_relationships: &[DirectRelationship],
    chained_relationships: &[ChainedRelationship],
    configuration: &GroupDetectionConfiguration,
    recent_window_days: i64,
) -> Vec<ScoredRelationship> {
    let mut direct_by_pair: HashMap<(i64, i64), &DirectRelationship> = HashMap::new();

    for direct in direct_relationships {
        direct_by_pair.insert(ordered_pair(direct.pilot_a, direct.pilot_b), direct);
    }

    let mut chains_by_pair: HashMap<(i64, i64), Vec<&ChainedRelationship>> = HashMap::new();

    for chain in chained_relationships {
        chains_by_pair
            .entry(ordered_pair(chain.pilot_a, chain.pilot_c))
            .or_default()
            .push(chain);
    }

    let mut pairs: HashSet<(i64, i64)> = direct_by_pair.keys().copied().collect();
    pairs.extend(chains_by_pair.keys().copied());

    let mut relationships = Vec::new();

    for pair in pairs {
        let direct = direct_by_pair.get(&pair).copied();
        let chain_candidates = chains_by_pair.get(&pair);

        let best_chain = chain_candidates.and_then(|candidates| {
            best_chain_for_pair(candidates, configuration, recent_window_days)
        });

        let direct_strength_raw = direct.map(|relationship| {
            direct_strength(
                relationship.counted_shared_kills,
                configuration.minimum_shared_events,
                configuration.strength_step,
            )
        });

        let chain_wins = match (direct_strength_raw, &best_chain) {
            (Some(direct_strength), Some((chain_strength, _, _))) => *chain_strength > direct_strength,
            (None, Some(_)) => true,
            _ => false,
        };

        if chain_wins {
            let (chain_strength, chain_confidence, _winning_chain) =
                best_chain.expect("chain_wins implies a winning chain");
            let intermediaries_in_scan = intermediaries_in_scan_for_pair(
                chain_candidates.expect("chain_wins implies chain candidates exist"),
            );

            relationships.push(ScoredRelationship {
                pilot_a: pair.0,
                pilot_b: pair.1,
                link_type: RelationshipLinkType::Chain,
                strength: round_to_integer(chain_strength),
                confidence: round_to_integer(chain_confidence),
                total_shared_kills: None,
                last_shared_kill_time_utc: None,
                intermediaries_in_scan: Some(intermediaries_in_scan),
            });
        } else if let Some(relationship) = direct {
            let confidence = direct_confidence(
                &relationship.counted_shared_kill_gang_sizes,
                relationship.split_bonus_applied,
                relationship.counted_shared_kills,
                configuration.minimum_shared_events,
                configuration,
            );

            relationships.push(ScoredRelationship {
                pilot_a: pair.0,
                pilot_b: pair.1,
                link_type: RelationshipLinkType::Direct,
                strength: round_to_integer(direct_strength_raw.expect("direct present")),
                confidence: round_to_integer(confidence),
                total_shared_kills: Some(relationship.counted_shared_kills),
                last_shared_kill_time_utc: Some(relationship.last_counted_kill_time_utc.clone()),
                intermediaries_in_scan: None,
            });
        }
    }

    relationships.sort_by(|left, right| left.pilot_a.cmp(&right.pilot_a).then(left.pilot_b.cmp(&right.pilot_b)));
    relationships
}

fn best_chain_for_pair<'a>(
    candidates: &[&'a ChainedRelationship],
    configuration: &GroupDetectionConfiguration,
    recent_window_days: i64,
) -> Option<(f64, f64, &'a ChainedRelationship)> {
    candidates
        .iter()
        .map(|chain| {
            let link_ab = score_link(
                chain.link_ab_counted_shared_kills,
                chain.link_ab_split_bonus_applied,
                &chain.link_ab_counted_shared_kill_gang_sizes,
                configuration,
            );
            let link_cb = score_link(
                chain.link_cb_counted_shared_kills,
                chain.link_cb_split_bonus_applied,
                &chain.link_cb_counted_shared_kill_gang_sizes,
                configuration,
            );

            let weaker_strength = link_ab.strength.min(link_cb.strength);
            let weaker_confidence = link_ab.confidence.min(link_cb.confidence);

            let strength = chain_strength(
                weaker_strength,
                configuration.chain_discount,
                chain.chain_age_days,
                recent_window_days,
            );

            (strength, weaker_confidence, *chain)
        })
        .max_by(|left, right| left.0.partial_cmp(&right.0).expect("chain strength is never NaN"))
        .map(|(strength, weaker_confidence, chain)| {
            let distinct_intermediary_count = candidates.len() as i64;
            let confidence = chain_confidence(
                weaker_confidence,
                distinct_intermediary_count,
                configuration.intermediary_bonus.per_additional,
                configuration.intermediary_bonus.maximum,
            );

            (strength, confidence, chain)
        })
}

fn intermediaries_in_scan_for_pair(candidates: &[&ChainedRelationship]) -> Vec<i64> {
    let mut intermediaries: Vec<i64> = candidates
        .iter()
        .filter(|chain| chain.intermediary_in_scan)
        .map(|chain| chain.intermediary_pilot_b)
        .collect();

    intermediaries.sort_unstable();
    intermediaries.dedup();
    intermediaries
}

fn score_link(
    counted_shared_kills: i64,
    split_bonus_applied: bool,
    counted_shared_kill_gang_sizes: &[i64],
    configuration: &GroupDetectionConfiguration,
) -> LinkScore {
    LinkScore {
        strength: direct_strength(
            counted_shared_kills,
            configuration.minimum_shared_events,
            configuration.strength_step,
        ),
        confidence: direct_confidence(
            counted_shared_kill_gang_sizes,
            split_bonus_applied,
            counted_shared_kills,
            configuration.minimum_shared_events,
            configuration,
        ),
    }
}

pub(crate) fn direct_strength(counted_shared_kills: i64, minimum_shared_events: i64, strength_step: i32) -> f64 {
    let levels_above_minimum = counted_shared_kills - minimum_shared_events + 1;
    (strength_step as f64 * levels_above_minimum as f64).min(100.0)
}

pub(crate) fn direct_confidence(
    counted_shared_kill_gang_sizes: &[i64],
    split_bonus_applied: bool,
    counted_shared_kills: i64,
    minimum_shared_events: i64,
    configuration: &GroupDetectionConfiguration,
) -> f64 {
    let gang_quality = 100.0 * average_gang_size_weight(counted_shared_kill_gang_sizes, &configuration.gang_size_weights);
    let sample = sample_factor(
        counted_shared_kills,
        minimum_shared_events,
        configuration.sample_factor.minimum,
        configuration.sample_factor.maximum,
        configuration.sample_factor.saturates_at_counted_shared_kills,
    );
    let bonus = if split_bonus_applied { configuration.split_bonus as f64 } else { 0.0 };

    (gang_quality * sample + bonus).min(100.0)
}

pub(crate) fn average_gang_size_weight(gang_sizes: &[i64], weights: &[GangSizeWeight]) -> f64 {
    if gang_sizes.is_empty() {
        return 0.0;
    }

    let total: f64 = gang_sizes.iter().map(|gang_size| gang_size_weight(*gang_size, weights)).sum();
    total / gang_sizes.len() as f64
}

fn gang_size_weight(gang_size: i64, weights: &[GangSizeWeight]) -> f64 {
    weights
        .iter()
        .find(|band| gang_size <= band.maximum_gang_size)
        .or_else(|| weights.last())
        .map(|band| band.weight)
        .unwrap_or(0.0)
}

pub(crate) fn sample_factor(
    counted_shared_kills: i64,
    minimum_shared_events: i64,
    minimum: f64,
    maximum: f64,
    saturates_at_counted_shared_kills: i64,
) -> f64 {
    if counted_shared_kills <= minimum_shared_events {
        return minimum;
    }

    if counted_shared_kills >= saturates_at_counted_shared_kills {
        return maximum;
    }

    let progress = (counted_shared_kills - minimum_shared_events) as f64
        / (saturates_at_counted_shared_kills - minimum_shared_events) as f64;

    minimum + (maximum - minimum) * progress
}

pub(crate) fn chain_strength(weaker_link_strength: f64, chain_discount: f64, chain_age_days: f64, recent_window_days: i64) -> f64 {
    let window = recent_window_days.max(1) as f64;
    let age_decay = (1.0 - chain_age_days / window).max(0.0);

    (weaker_link_strength * chain_discount * age_decay).min(100.0).max(0.0)
}

pub(crate) fn intermediary_bonus(distinct_intermediary_count: i64, per_additional: i32, maximum_bonus: i32) -> f64 {
    let additional = (distinct_intermediary_count - 1).max(0);
    (additional as f64 * per_additional as f64).min(maximum_bonus as f64)
}

pub(crate) fn chain_confidence(weaker_link_confidence: f64, distinct_intermediary_count: i64, per_additional: i32, maximum_bonus: i32) -> f64 {
    (weaker_link_confidence + intermediary_bonus(distinct_intermediary_count, per_additional, maximum_bonus)).min(100.0)
}

pub(crate) fn round_to_integer(value: f64) -> i32 {
    value.max(0.0).trunc() as i32
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::group_analysis::group_detection_configuration::{
        IntermediaryBonusConfiguration, SampleFactorConfiguration,
    };

    fn configuration() -> GroupDetectionConfiguration {
        GroupDetectionConfiguration {
            minimum_shared_events: 2,
            npc_corporation_id_threshold: 1_005_000,
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

    fn direct(pilot_a: i64, pilot_b: i64, counted_shared_kills: i64, gang_sizes: Vec<i64>, split_bonus_applied: bool) -> DirectRelationship {
        DirectRelationship {
            pilot_a,
            pilot_b,
            counted_shared_kills,
            last_counted_kill_time_utc: "2026-09-20T00:00:00+00:00".to_string(),
            split_bonus_applied,
            counted_shared_kill_gang_sizes: gang_sizes,
        }
    }

    fn chain(
        pilot_a: i64,
        pilot_c: i64,
        intermediary_pilot_b: i64,
        intermediary_in_scan: bool,
        link_counted_shared_kills: i64,
        link_gang_sizes: Vec<i64>,
        chain_age_days: f64,
    ) -> ChainedRelationship {
        ChainedRelationship {
            pilot_a,
            pilot_c,
            intermediary_pilot_b,
            intermediary_in_scan,
            link_ab_counted_shared_kills: link_counted_shared_kills,
            link_ab_most_recent_in_window_kill_time_utc: "2026-09-20T00:00:00+00:00".to_string(),
            link_ab_split_bonus_applied: false,
            link_ab_counted_shared_kill_gang_sizes: link_gang_sizes.clone(),
            link_cb_counted_shared_kills: link_counted_shared_kills,
            link_cb_most_recent_in_window_kill_time_utc: "2026-09-20T00:00:00+00:00".to_string(),
            link_cb_split_bonus_applied: false,
            link_cb_counted_shared_kill_gang_sizes: link_gang_sizes,
            chain_age_days,
        }
    }

    #[test]
    fn direct_confidence_duo_two_kills_two_to_three_attackers_is_fifty() {
        let confidence = direct_confidence(&[2, 3], false, 2, 2, &configuration());
        assert_eq!(round_to_integer(confidence), 50);
    }

    #[test]
    fn direct_confidence_trio_ten_kills_two_to_three_attackers_is_one_hundred() {
        let confidence = direct_confidence(&[2; 10], false, 10, 2, &configuration());
        assert_eq!(round_to_integer(confidence), 100);
    }

    #[test]
    fn direct_confidence_same_roam_ten_kills_eight_to_ten_attackers_is_twenty_five() {
        let confidence = direct_confidence(&[9; 10], false, 10, 2, &configuration());
        assert_eq!(round_to_integer(confidence), 25);
    }

    #[test]
    fn direct_confidence_mixed_six_kills_half_four_to_five_half_eight_to_ten_is_thirty_seven() {
        let gang_sizes = vec![4, 4, 4, 9, 9, 9];
        let confidence = direct_confidence(&gang_sizes, false, 6, 2, &configuration());
        assert_eq!(round_to_integer(confidence), 37);
    }

    #[test]
    fn direct_confidence_ex_corporation_pair_four_kills_four_to_five_attackers_split_is_sixty_six() {
        let confidence = direct_confidence(&[4, 4, 4, 4], true, 4, 2, &configuration());
        assert_eq!(round_to_integer(confidence), 66);
    }

    #[test]
    fn direct_strength_at_minimum_is_ten() {
        assert_eq!(round_to_integer(direct_strength(2, 2, 10)), 10);
    }

    #[test]
    fn direct_strength_caps_at_one_hundred() {
        assert_eq!(round_to_integer(direct_strength(20, 2, 10)), 100);
    }

    #[test]
    fn chain_strength_at_zero_age_applies_only_discount() {
        assert_eq!(chain_strength(100.0, 0.75, 0.0, 14), 75.0);
    }

    #[test]
    fn chain_strength_decays_to_zero_at_window_end() {
        assert_eq!(chain_strength(100.0, 0.75, 14.0, 14), 0.0);
    }

    #[test]
    fn direct_pair_with_no_chain_reports_direct() {
        let direct_relationships = vec![direct(100, 200, 5, vec![3, 3, 3, 3, 3], false)];

        let relationships = score_and_select_relationships(&direct_relationships, &[], &configuration(), 14);

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].link_type, RelationshipLinkType::Direct);
        assert_eq!(relationships[0].total_shared_kills, Some(5));
        assert!(relationships[0].intermediaries_in_scan.is_none());
    }

    #[test]
    fn chain_only_pair_at_saturated_link_strength_reports_chain_strength_seventy_five_confidence_one_hundred() {
        let chained_relationships = vec![chain(100, 300, 200, true, 11, vec![2; 11], 0.0)];

        let relationships = score_and_select_relationships(&[], &chained_relationships, &configuration(), 14);

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].link_type, RelationshipLinkType::Chain);
        assert!(relationships[0].total_shared_kills.is_none());
        assert_eq!(relationships[0].intermediaries_in_scan, Some(vec![200]));
        assert_eq!(relationships[0].strength, 75);
        assert_eq!(relationships[0].confidence, 100);
    }

    #[test]
    fn stronger_chain_wins_over_weaker_direct_for_the_same_pair() {
        let direct_relationships = vec![direct(100, 300, 2, vec![9, 9], false)];
        let chained_relationships = vec![chain(100, 300, 200, false, 10, vec![2; 10], 0.0)];

        let relationships =
            score_and_select_relationships(&direct_relationships, &chained_relationships, &configuration(), 14);

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].link_type, RelationshipLinkType::Chain);
    }

    #[test]
    fn chain_strength_below_direct_strength_resolves_to_direct() {
        let direct_relationships = vec![direct(100, 300, 2, vec![2, 2], false)];
        let chained_relationships = vec![chain(100, 300, 200, false, 2, vec![2, 2], 0.0)];

        let relationships =
            score_and_select_relationships(&direct_relationships, &chained_relationships, &configuration(), 14);

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].link_type, RelationshipLinkType::Direct);
    }

    #[test]
    fn chain_confidence_with_five_intermediaries_adds_capped_thirty_point_bonus_to_weaker_link_confidence_fifty() {
        let chained_relationships = vec![
            chain(100, 300, 200, true, 10, vec![6; 10], 0.0),
            chain(100, 300, 400, false, 10, vec![6; 10], 0.0),
            chain(100, 300, 500, false, 10, vec![6; 10], 0.0),
            chain(100, 300, 600, false, 10, vec![6; 10], 0.0),
            chain(100, 300, 700, false, 10, vec![6; 10], 0.0),
        ];

        let relationships = score_and_select_relationships(&[], &chained_relationships, &configuration(), 14);

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].confidence, 80);
    }
}
