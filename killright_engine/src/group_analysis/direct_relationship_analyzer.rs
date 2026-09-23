use std::collections::HashMap;

use crate::repositories::killmail_relationship_repository::KillmailAttackerEvidence;
use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;

pub const PROVISIONAL_NPC_CORPORATION_ID_THRESHOLD: i64 = 1_005_000;
pub const PROVISIONAL_MINIMUM_SHARED_EVENTS: i64 = 2;

#[derive(Debug, Clone, PartialEq)]
pub struct DirectRelationship {
    pub pilot_a: i64,
    pub pilot_b: i64,
    pub counted_shared_kills: i64,
    pub last_counted_kill_time_utc: String,
}

struct SharedKillEvent {
    kill_time_utc: String,
    is_same_corporation_or_alliance: bool,
}

pub fn analyze_direct_relationships(
    evidence: &[KillmailAttackerEvidence],
    current_identities: &[PilotIdentitySnapshot],
    npc_corporation_id_threshold: i64,
    minimum_shared_events: i64,
) -> Vec<DirectRelationship> {
    let current_identity_by_character = build_current_identity_index(current_identities);
    let attackers_by_killmail = group_attackers_by_killmail(evidence);
    let shared_events_by_pair =
        build_shared_events_by_pair(&attackers_by_killmail, npc_corporation_id_threshold);

    let mut relationships = Vec::new();

    for ((pilot_a, pilot_b), mut events) in shared_events_by_pair {
        if is_currently_same_corporation_or_alliance(
            &current_identity_by_character,
            pilot_a,
            pilot_b,
            npc_corporation_id_threshold,
        ) {
            continue;
        }

        events.sort_by(|left, right| left.kill_time_utc.cmp(&right.kill_time_utc));

        let counted_events = apply_after_split_rule(&events);

        if (counted_events.len() as i64) < minimum_shared_events {
            continue;
        }

        let last_counted_kill_time_utc = counted_events
            .iter()
            .map(|event| event.kill_time_utc.clone())
            .max()
            .expect("counted_events is non-empty after the threshold check");

        relationships.push(DirectRelationship {
            pilot_a,
            pilot_b,
            counted_shared_kills: counted_events.len() as i64,
            last_counted_kill_time_utc,
        });
    }

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

fn build_shared_events_by_pair(
    attackers_by_killmail: &HashMap<i64, Vec<&KillmailAttackerEvidence>>,
    npc_corporation_id_threshold: i64,
) -> HashMap<(i64, i64), Vec<SharedKillEvent>> {
    let mut shared_events: HashMap<(i64, i64), Vec<SharedKillEvent>> = HashMap::new();

    for attackers in attackers_by_killmail.values() {
        for i in 0..attackers.len() {
            for j in (i + 1)..attackers.len() {
                let first = attackers[i];
                let second = attackers[j];

                let pair_key = ordered_pair(first.character_id, second.character_id);

                let is_same = is_same_corporation_or_alliance(
                    first.corporation_id,
                    first.alliance_id,
                    second.corporation_id,
                    second.alliance_id,
                    npc_corporation_id_threshold,
                );

                shared_events
                    .entry(pair_key)
                    .or_default()
                    .push(SharedKillEvent {
                        kill_time_utc: first.kill_time_utc.clone(),
                        is_same_corporation_or_alliance: is_same,
                    });
            }
        }
    }

    shared_events
}

fn ordered_pair(character_a: i64, character_b: i64) -> (i64, i64) {
    if character_a <= character_b {
        (character_a, character_b)
    } else {
        (character_b, character_a)
    }
}

fn build_current_identity_index(
    current_identities: &[PilotIdentitySnapshot],
) -> HashMap<i64, &PilotIdentitySnapshot> {
    let mut index = HashMap::new();

    for identity in current_identities {
        if let Some(character_id) = identity.character_id {
            index.insert(character_id, identity);
        }
    }

    index
}

fn is_currently_same_corporation_or_alliance(
    current_identity_by_character: &HashMap<i64, &PilotIdentitySnapshot>,
    pilot_a: i64,
    pilot_b: i64,
    npc_corporation_id_threshold: i64,
) -> bool {
    let (Some(identity_a), Some(identity_b)) = (
        current_identity_by_character.get(&pilot_a),
        current_identity_by_character.get(&pilot_b),
    ) else {
        return false;
    };

    is_same_corporation_or_alliance(
        identity_a.corporation_id,
        identity_a.alliance_id,
        identity_b.corporation_id,
        identity_b.alliance_id,
        npc_corporation_id_threshold,
    )
}

fn apply_after_split_rule(events_sorted_by_time: &[SharedKillEvent]) -> Vec<&SharedKillEvent> {
    let mut seen_same = false;
    let mut split_detected = false;

    for event in events_sorted_by_time {
        if event.is_same_corporation_or_alliance {
            seen_same = true;
        } else if seen_same {
            split_detected = true;
        }
    }

    if split_detected {
        events_sorted_by_time.iter().collect()
    } else {
        events_sorted_by_time
            .iter()
            .filter(|event| !event.is_same_corporation_or_alliance)
            .collect()
    }
}

fn is_same_corporation_or_alliance(
    corporation_a: Option<i64>,
    alliance_a: Option<i64>,
    corporation_b: Option<i64>,
    alliance_b: Option<i64>,
    npc_corporation_id_threshold: i64,
) -> bool {
    if let (Some(alliance_a), Some(alliance_b)) = (alliance_a, alliance_b) {
        if alliance_a == alliance_b {
            return true;
        }
    }

    if let (Some(corporation_a), Some(corporation_b)) = (corporation_a, corporation_b) {
        if corporation_a == corporation_b && corporation_a >= npc_corporation_id_threshold {
            return true;
        }
    }

    false
}

#[cfg(test)]
mod tests {
    use super::*;

    const NPC_THRESHOLD: i64 = 1_005_000;
    const MINIMUM_SHARED_EVENTS: i64 = 2;
    const SHARED_CORP: i64 = 5_000_009;
    const GATE_CORP: i64 = 5_000_005;

    fn evidence(
        killmail_id: i64,
        character_id: i64,
        corporation_id: Option<i64>,
        alliance_id: Option<i64>,
        kill_time_utc: &str,
    ) -> KillmailAttackerEvidence {
        KillmailAttackerEvidence {
            killmail_id,
            character_id,
            corporation_id,
            alliance_id,
            kill_time_utc: kill_time_utc.to_string(),
        }
    }

    fn identity(character_id: i64, corporation_id: Option<i64>, alliance_id: Option<i64>) -> PilotIdentitySnapshot {
        PilotIdentitySnapshot {
            input_name: character_id.to_string(),
            character_id: Some(character_id),
            character_name: Some(character_id.to_string()),
            verify_status: "Verified".to_string(),
            security_status: None,
            corporation_id,
            corporation_name: None,
            corporation_ticker: None,
            alliance_id,
            alliance_name: None,
            alliance_ticker: None,
            cached_at_utc: "2026-09-20T00:00:00+00:00".to_string(),
        }
    }

    #[test]
    fn never_same_corporation_or_alliance_all_shared_kills_count() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), None, "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), None, "2026-09-10T00:00:00+00:00"),
            evidence(2, 100, Some(1), None, "2026-09-12T00:00:00+00:00"),
            evidence(2, 200, Some(2), None, "2026-09-12T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].counted_shared_kills, 2);
    }

    #[test]
    fn currently_same_corporation_or_alliance_pair_is_gated_out() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), None, "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), None, "2026-09-10T00:00:00+00:00"),
            evidence(2, 100, Some(1), None, "2026-09-12T00:00:00+00:00"),
            evidence(2, 200, Some(2), None, "2026-09-12T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(GATE_CORP), None), identity(200, Some(GATE_CORP), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn split_with_shared_kill_since_counts_all_shared_kills_including_corp_mate_period() {
        let evidence_rows = vec![
            evidence(1, 100, Some(SHARED_CORP), None, "2026-09-01T00:00:00+00:00"),
            evidence(1, 200, Some(SHARED_CORP), None, "2026-09-01T00:00:00+00:00"),
            evidence(2, 100, Some(SHARED_CORP), None, "2026-09-02T00:00:00+00:00"),
            evidence(2, 200, Some(SHARED_CORP), None, "2026-09-02T00:00:00+00:00"),
            evidence(3, 100, Some(1), None, "2026-09-15T00:00:00+00:00"),
            evidence(3, 200, Some(2), None, "2026-09-15T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].counted_shared_kills, 3);
        assert_eq!(relationships[0].last_counted_kill_time_utc, "2026-09-15T00:00:00+00:00");
    }

    #[test]
    fn split_with_no_shared_kill_since_counts_only_earlier_not_same_kills() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), None, "2026-09-01T00:00:00+00:00"),
            evidence(1, 200, Some(2), None, "2026-09-01T00:00:00+00:00"),
            evidence(2, 100, Some(1), None, "2026-09-03T00:00:00+00:00"),
            evidence(2, 200, Some(2), None, "2026-09-03T00:00:00+00:00"),
            evidence(3, 100, Some(SHARED_CORP), None, "2026-09-10T00:00:00+00:00"),
            evidence(3, 200, Some(SHARED_CORP), None, "2026-09-10T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].counted_shared_kills, 2);
        assert_eq!(relationships[0].last_counted_kill_time_utc, "2026-09-03T00:00:00+00:00");
    }

    #[test]
    fn split_with_only_one_earlier_not_same_kill_stays_below_threshold() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), None, "2026-09-01T00:00:00+00:00"),
            evidence(1, 200, Some(2), None, "2026-09-01T00:00:00+00:00"),
            evidence(2, 100, Some(SHARED_CORP), None, "2026-09-10T00:00:00+00:00"),
            evidence(2, 200, Some(SHARED_CORP), None, "2026-09-10T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn same_npc_corporation_pair_analysis_remains_enabled() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1000), None, "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(1000), None, "2026-09-10T00:00:00+00:00"),
            evidence(2, 100, Some(1000), None, "2026-09-12T00:00:00+00:00"),
            evidence(2, 200, Some(1000), None, "2026-09-12T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1000), None), identity(200, Some(1000), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].counted_shared_kills, 2);
    }

    #[test]
    fn alliance_match_across_different_corporations_counts_as_same() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), Some(50), "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), Some(50), "2026-09-10T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert!(relationships.is_empty());
    }

    #[test]
    fn below_minimum_shared_events_threshold_produces_no_relationship() {
        let evidence_rows = vec![
            evidence(1, 100, Some(1), None, "2026-09-10T00:00:00+00:00"),
            evidence(1, 200, Some(2), None, "2026-09-10T00:00:00+00:00"),
        ];
        let identities = vec![identity(100, Some(1), None), identity(200, Some(2), None)];

        let relationships = analyze_direct_relationships(
            &evidence_rows,
            &identities,
            NPC_THRESHOLD,
            MINIMUM_SHARED_EVENTS,
        );

        assert!(relationships.is_empty());
    }
}
