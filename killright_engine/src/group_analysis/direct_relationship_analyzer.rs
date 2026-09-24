use crate::group_analysis::shared_relationship_evidence::{
    apply_after_split_rule, build_current_identity_index, build_shared_events_by_pair,
    group_attackers_by_killmail, is_currently_same_corporation_or_alliance,
};
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
