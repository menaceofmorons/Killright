use duckdb::{params, Connection, OptionalExt, Result};

use crate::confidence::Confidence;
use crate::pilot_to_pilot_strength::Strength;

/// Design_Spec_Dense.md §6.11.4: one single-hop A-B-C chain result --
/// `chain_strength`/`chain_confidence` are the min-combine of the two
/// contributing links; `final_strength`/`final_confidence` are the
/// max-combine of that chain value against any direct A-C relationship.
/// Chains beyond one hop (A-B-C-D) and folding multiple independent bridges
/// for the same pair are both §6.11.7 UNRESOLVED -- out of scope here.
pub struct TransitiveChainResult {
    pub bridge_pilot_id: i64,
    pub chain_strength: Strength,
    pub chain_confidence: Confidence,
    pub final_strength: Strength,
    pub final_confidence: Confidence,
}

fn parse_strength(code: &str) -> Strength {
    match code {
        "W" => Strength::Weak,
        "M" => Strength::Medium,
        "S" => Strength::Strong,
        "VS" => Strength::VeryStrong,
        other => unreachable!("historic_relationship_classification.strength code '{other}' is not W/M/S/VS (Design_Spec_Dense.md §4.8)"),
    }
}

fn parse_confidence(code: &str) -> Confidence {
    match code {
        "Low" => Confidence::Low,
        "Average" => Confidence::Average,
        "High" => Confidence::High,
        other => unreachable!("historic_relationship_classification.confidence code '{other}' is not Low/Average/High (Design_Spec_Dense.md §4.8)"),
    }
}

/// Every Entity Type P partner directly linked to `pilot_id`, read from
/// either canonical side of historic_relationship_classification's
/// pilot_id/entity_id pair (Design_Spec_Dense.md §4.7's lower-character-ID-
/// first ordering).
pub fn fetch_direct_p2p_partners(connection: &Connection, pilot_id: i64) -> Result<Vec<(i64, Strength, Confidence)>> {
    let mut statement = connection.prepare(
        "SELECT entity_id, strength, confidence FROM historic_relationship_classification \
         WHERE entity_type = 'P' AND pilot_id = ? \
         UNION ALL \
         SELECT pilot_id, strength, confidence FROM historic_relationship_classification \
         WHERE entity_type = 'P' AND entity_id = ?;",
    )?;

    let rows = statement.query_map(params![pilot_id, pilot_id], |row| Ok((row.get::<_, i64>(0)?, row.get::<_, String>(1)?, row.get::<_, String>(2)?)))?;

    let mut partners = Vec::new();
    for row in rows {
        let (partner_id, strength_code, confidence_code) = row?;
        partners.push((partner_id, parse_strength(&strength_code), parse_confidence(&confidence_code)));
    }

    Ok(partners)
}

/// The direct pilot_a_id-pilot_c_id Entity Type P relationship, if one is
/// stored, checking both canonical sides.
pub fn fetch_direct_p2p_relationship(connection: &Connection, pilot_a_id: i64, pilot_c_id: i64) -> Result<Option<(Strength, Confidence)>> {
    connection
        .query_row(
            "SELECT strength, confidence FROM historic_relationship_classification \
             WHERE entity_type = 'P' AND ((pilot_id = ? AND entity_id = ?) OR (pilot_id = ? AND entity_id = ?));",
            params![pilot_a_id, pilot_c_id, pilot_c_id, pilot_a_id],
            |row| Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?)),
        )
        .optional()
        .map(|raw| raw.map(|(strength_code, confidence_code)| (parse_strength(&strength_code), parse_confidence(&confidence_code))))
}

/// Design_Spec_Dense.md §6.11.4: "within chain A-B-C, inferred A-C strength
/// takes weaker of two links... Confidence combines the same way... When
/// pair has both a direct relationship and a chain-inferred one, final
/// rating takes stronger of the two." One result per bridge pilot directly
/// linked to both pilot_a_id and pilot_c_id (excluding pilot_a_id/pilot_c_id
/// themselves) -- never persisted (§6.11.2, §4.8), computed fresh on every
/// call, sorted by bridge_pilot_id for deterministic log output.
pub fn infer_transitive_p2p_chains(connection: &Connection, pilot_a_id: i64, pilot_c_id: i64) -> Result<Vec<TransitiveChainResult>> {
    let direct = fetch_direct_p2p_relationship(connection, pilot_a_id, pilot_c_id)?;
    let a_partners = fetch_direct_p2p_partners(connection, pilot_a_id)?;
    let c_partners = fetch_direct_p2p_partners(connection, pilot_c_id)?;

    let mut results = Vec::new();

    for &(bridge_pilot_id, a_to_bridge_strength, a_to_bridge_confidence) in &a_partners {
        if bridge_pilot_id == pilot_a_id || bridge_pilot_id == pilot_c_id {
            continue;
        }

        let bridge_to_c = c_partners.iter().find(|&&(candidate_id, _, _)| candidate_id == bridge_pilot_id);

        let (bridge_to_c_strength, bridge_to_c_confidence) = match bridge_to_c {
            Some(&(_, strength, confidence)) => (strength, confidence),
            None => continue,
        };

        let chain_strength = a_to_bridge_strength.min(bridge_to_c_strength);
        let chain_confidence = a_to_bridge_confidence.min(bridge_to_c_confidence);

        let (final_strength, final_confidence) = match direct {
            Some((direct_strength, direct_confidence)) => (chain_strength.max(direct_strength), chain_confidence.max(direct_confidence)),
            None => (chain_strength, chain_confidence),
        };

        results.push(TransitiveChainResult {
            bridge_pilot_id,
            chain_strength,
            chain_confidence,
            final_strength,
            final_confidence,
        });
    }

    results.sort_by_key(|result| result.bridge_pilot_id);
    Ok(results)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    fn insert_classification_row(connection: &Connection, pilot_id: i64, entity_id: i64, strength_code: &str, confidence_code: &str) {
        connection
            .execute(
                "INSERT INTO historic_relationship_classification \
                 (pilot_id, entity_id, entity_type, strength, confidence, last_computed_utc) \
                 VALUES (?, ?, 'P', ?, ?, '2026-09-14T00:00:00Z');",
                params![pilot_id, entity_id, strength_code, confidence_code],
            )
            .unwrap();
    }

    const PILOT_A: i64 = 95465499;
    const PILOT_B: i64 = 90379338;
    const PILOT_C: i64 = 91657700;
    const PILOT_D: i64 = 98765432;

    #[test]
    fn fetch_direct_p2p_partners_returns_partners_from_both_canonical_sides() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_B, "S", "Low");
        insert_classification_row(&connection, PILOT_C, PILOT_A, "W", "Average");

        let partners = fetch_direct_p2p_partners(&connection, PILOT_A).unwrap();

        assert_eq!(partners.len(), 2);
        assert!(partners.contains(&(PILOT_B, Strength::Strong, Confidence::Low)));
        assert!(partners.contains(&(PILOT_C, Strength::Weak, Confidence::Average)));
    }

    #[test]
    fn fetch_direct_p2p_relationship_finds_a_row_regardless_of_argument_order() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_B, "S", "High");

        assert_eq!(fetch_direct_p2p_relationship(&connection, PILOT_A, PILOT_B).unwrap(), Some((Strength::Strong, Confidence::High)));
        assert_eq!(fetch_direct_p2p_relationship(&connection, PILOT_B, PILOT_A).unwrap(), Some((Strength::Strong, Confidence::High)));
    }

    #[test]
    fn fetch_direct_p2p_relationship_none_when_no_row_exists() {
        let connection = open_test_schema();

        assert_eq!(fetch_direct_p2p_relationship(&connection, PILOT_A, PILOT_B).unwrap(), None);
    }

    #[test]
    fn infer_transitive_p2p_chains_min_combines_a_single_bridge_chain_with_no_direct_relationship() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_B, "VS", "High");
        insert_classification_row(&connection, PILOT_B, PILOT_C, "W", "Low");

        let chains = infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap();

        assert_eq!(chains.len(), 1);
        assert_eq!(chains[0].bridge_pilot_id, PILOT_B);
        assert_eq!(chains[0].chain_strength, Strength::Weak);
        assert_eq!(chains[0].chain_confidence, Confidence::Low);
        assert_eq!(chains[0].final_strength, Strength::Weak);
        assert_eq!(chains[0].final_confidence, Confidence::Low);
    }

    #[test]
    fn infer_transitive_p2p_chains_max_combines_the_chain_against_an_existing_direct_relationship() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_B, "VS", "High");
        insert_classification_row(&connection, PILOT_B, PILOT_C, "W", "Low");
        insert_classification_row(&connection, PILOT_A, PILOT_C, "S", "Average");

        let chains = infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap();

        assert_eq!(chains.len(), 1);
        assert_eq!(chains[0].chain_strength, Strength::Weak);
        assert_eq!(chains[0].chain_confidence, Confidence::Low);
        assert_eq!(chains[0].final_strength, Strength::Strong, "final strength must take the stronger of chain (Weak) vs direct (Strong)");
        assert_eq!(chains[0].final_confidence, Confidence::Average, "final confidence must take the stronger of chain (Low) vs direct (Average)");
    }

    #[test]
    fn infer_transitive_p2p_chains_excludes_a_partner_not_linked_to_both_pilots() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_B, "S", "Low");
        insert_classification_row(&connection, PILOT_B, PILOT_D, "S", "Low");

        let chains = infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap();

        assert!(chains.is_empty(), "PILOT_B is not linked to PILOT_C, so no chain exists");
    }

    #[test]
    fn infer_transitive_p2p_chains_does_not_treat_the_direct_counterpart_as_its_own_bridge() {
        let connection = open_test_schema();
        insert_classification_row(&connection, PILOT_A, PILOT_C, "S", "Low");

        let chains = infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap();

        assert!(chains.is_empty(), "a direct A-C relationship must not surface as a degenerate A-C-C or A-A-C chain");
    }

    #[test]
    fn infer_transitive_p2p_chains_returns_multiple_bridges_sorted_by_bridge_pilot_id() {
        let connection = open_test_schema();
        let higher_bridge = PILOT_B + 1_000_000;
        insert_classification_row(&connection, PILOT_A, higher_bridge, "S", "Low");
        insert_classification_row(&connection, higher_bridge, PILOT_C, "S", "Low");
        insert_classification_row(&connection, PILOT_A, PILOT_B, "M", "Low");
        insert_classification_row(&connection, PILOT_B, PILOT_C, "M", "Low");

        let chains = infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap();

        assert_eq!(chains.len(), 2);
        assert_eq!(chains[0].bridge_pilot_id, PILOT_B);
        assert_eq!(chains[1].bridge_pilot_id, higher_bridge);
    }

    #[test]
    fn infer_transitive_p2p_chains_empty_when_no_bridge_links_both_pilots() {
        let connection = open_test_schema();

        assert!(infer_transitive_p2p_chains(&connection, PILOT_A, PILOT_C).unwrap().is_empty());
    }
}
