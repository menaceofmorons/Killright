use std::collections::HashMap;

use crate::repositories::killmail_relationship_repository::KillmailAttackerEvidence;
use crate::repositories::pilot_identity_repository::PilotIdentitySnapshot;

#[derive(Clone)]
pub(crate) struct SharedKillEvent {
    pub killmail_id: i64,
    pub kill_time_utc: String,
    pub is_same_corporation_or_alliance: bool,
    pub unique_attacker_count: i64,
}

pub(crate) fn group_attackers_by_killmail(
    evidence: &[KillmailAttackerEvidence],
) -> HashMap<i64, Vec<&KillmailAttackerEvidence>> {
    let mut grouped: HashMap<i64, Vec<&KillmailAttackerEvidence>> = HashMap::new();

    for row in evidence {
        grouped.entry(row.killmail_id).or_default().push(row);
    }

    grouped
}

pub(crate) fn build_shared_events_by_pair(
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
                        killmail_id: first.killmail_id,
                        kill_time_utc: first.kill_time_utc.clone(),
                        is_same_corporation_or_alliance: is_same,
                        unique_attacker_count: first.unique_attacker_count,
                    });
            }
        }
    }

    shared_events
}

pub(crate) fn apply_after_split_rule(
    events_sorted_by_time: &[SharedKillEvent],
) -> (Vec<&SharedKillEvent>, bool) {
    let mut seen_same = false;
    let mut split_detected = false;

    for event in events_sorted_by_time {
        if event.is_same_corporation_or_alliance {
            seen_same = true;
        } else if seen_same {
            split_detected = true;
        }
    }

    let counted_events = if split_detected {
        events_sorted_by_time.iter().collect()
    } else {
        events_sorted_by_time
            .iter()
            .filter(|event| !event.is_same_corporation_or_alliance)
            .collect()
    };

    (counted_events, split_detected)
}

pub(crate) fn is_same_corporation_or_alliance(
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

pub(crate) fn build_current_identity_index(
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

pub(crate) fn is_currently_same_corporation_or_alliance(
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

pub(crate) fn ordered_pair(character_a: i64, character_b: i64) -> (i64, i64) {
    if character_a <= character_b {
        (character_a, character_b)
    } else {
        (character_b, character_a)
    }
}
