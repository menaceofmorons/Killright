use std::time::Instant;

use chrono::Utc;
use duckdb::{params, Connection, Result};

pub struct RebuildStats {
    pub summary_rows: i64,
    pub org_context_rows: i64,
    pub pair_event_scan_elapsed_ms: u128,
    pub summary_insert_elapsed_ms: u128,
    pub org_context_insert_elapsed_ms: u128,
    pub total_elapsed_ms: u128,
}

/// Fully rebuilds `historic_relationship_summary` and `historic_relationship_org_context`
/// from `historic_relationship_evidence` / `historic_relationship_evidence_participants`,
/// using one set-based `GROUP BY` pass over the evidence/participant self-join.
/// Both output tables are cleared and rewritten in full; this crate never performs
/// per-pair incremental summary maintenance (Seed-Mode semantics only).
pub fn rebuild_summary_and_org_context(connection: &Connection) -> Result<RebuildStats> {
    let total_start = Instant::now();
    let last_rebuilt_utc = Utc::now().to_rfc3339();

    let scan_start = Instant::now();
    connection.execute_batch(
        "DROP TABLE IF EXISTS pair_events;
         CREATE TEMP TABLE pair_events AS
         SELECT
             p1.character_id AS pilot_a_id,
             p2.character_id AS pilot_b_id,
             e.killmail_time_utc,
             CASE
                 WHEN p1.alliance_id IS NOT NULL OR p2.alliance_id IS NOT NULL THEN
                     (p1.alliance_id IS NOT NULL AND p2.alliance_id IS NOT NULL AND p1.alliance_id = p2.alliance_id)
                 ELSE
                     (p1.corporation_id IS NOT NULL AND p2.corporation_id IS NOT NULL AND p1.corporation_id = p2.corporation_id)
             END AS is_linked
         FROM historic_relationship_evidence e
         JOIN historic_relationship_evidence_participants p1 ON p1.evidence_id = e.evidence_id
         JOIN historic_relationship_evidence_participants p2
             ON p2.evidence_id = e.evidence_id AND p1.character_id < p2.character_id;",
    )?;
    let pair_event_scan_elapsed_ms = scan_start.elapsed().as_millis();

    let summary_start = Instant::now();
    connection.execute("DELETE FROM historic_relationship_summary;", [])?;
    connection.execute(
        "INSERT INTO historic_relationship_summary \
         (pilot_a_id, pilot_b_id, shared_event_count, first_seen_utc, last_seen_utc, last_rebuilt_utc) \
         SELECT pilot_a_id, pilot_b_id, COUNT(*), MIN(killmail_time_utc), MAX(killmail_time_utc), ? \
         FROM pair_events \
         GROUP BY pilot_a_id, pilot_b_id;",
        params![last_rebuilt_utc],
    )?;
    let summary_insert_elapsed_ms = summary_start.elapsed().as_millis();

    let org_context_start = Instant::now();
    connection.execute("DELETE FROM historic_relationship_org_context;", [])?;
    connection.execute(
        "INSERT INTO historic_relationship_org_context \
         (pilot_a_id, pilot_b_id, shared_event_count_linked, first_seen_linked_utc, last_seen_linked_utc, \
          shared_event_count_unlinked, first_seen_unlinked_utc, last_seen_unlinked_utc, last_rebuilt_utc) \
         SELECT \
             pilot_a_id, \
             pilot_b_id, \
             SUM(CASE WHEN is_linked THEN 1 ELSE 0 END), \
             MIN(CASE WHEN is_linked THEN killmail_time_utc END), \
             MAX(CASE WHEN is_linked THEN killmail_time_utc END), \
             SUM(CASE WHEN NOT is_linked THEN 1 ELSE 0 END), \
             MIN(CASE WHEN NOT is_linked THEN killmail_time_utc END), \
             MAX(CASE WHEN NOT is_linked THEN killmail_time_utc END), \
             ? \
         FROM pair_events \
         GROUP BY pilot_a_id, pilot_b_id;",
        params![last_rebuilt_utc],
    )?;
    let org_context_insert_elapsed_ms = org_context_start.elapsed().as_millis();

    connection.execute("DROP TABLE IF EXISTS pair_events;", [])?;

    let summary_rows: i64 = connection.query_row(
        "SELECT COUNT(*) FROM historic_relationship_summary;",
        [],
        |row| row.get(0),
    )?;
    let org_context_rows: i64 = connection.query_row(
        "SELECT COUNT(*) FROM historic_relationship_org_context;",
        [],
        |row| row.get(0),
    )?;

    Ok(RebuildStats {
        summary_rows,
        org_context_rows,
        pair_event_scan_elapsed_ms,
        summary_insert_elapsed_ms,
        org_context_insert_elapsed_ms,
        total_elapsed_ms: total_start.elapsed().as_millis(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn insert_evidence(connection: &Connection, evidence_id: i64, killmail_time_utc: &str, evidence_date_utc: &str) {
        connection
            .execute(
                "INSERT INTO historic_relationship_evidence \
                 (evidence_id, source_killmail_id, killmail_time_utc, evidence_date_utc, solar_system_id, \
                  victim_ship_type_id, participant_count, created_utc) \
                 VALUES (?, ?, ?, ?, NULL, NULL, 2, ?);",
                params![evidence_id, evidence_id, killmail_time_utc, evidence_date_utc, killmail_time_utc],
            )
            .unwrap();
    }

    fn insert_participant(
        connection: &Connection,
        evidence_id: i64,
        character_id: i64,
        corporation_id: Option<i64>,
        alliance_id: Option<i64>,
    ) {
        connection
            .execute(
                "INSERT INTO historic_relationship_evidence_participants \
                 (evidence_id, character_id, corporation_id, alliance_id, ship_type_id) \
                 VALUES (?, ?, ?, ?, NULL);",
                params![evidence_id, character_id, corporation_id, alliance_id],
            )
            .unwrap();
    }

    #[test]
    fn org_context_bucketing_matches_alliance_precedence_rule() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        // Event A: same alliance -> linked.
        insert_evidence(&connection, 1, "2025-01-01T00:00:00Z", "2025-01-01");
        insert_participant(&connection, 1, 1001, Some(7000), Some(5000));
        insert_participant(&connection, 1, 1002, Some(7000), Some(5000));

        // Event B: different alliances, same corp -> unlinked (alliance takes precedence).
        insert_evidence(&connection, 2, "2025-02-01T00:00:00Z", "2025-02-01");
        insert_participant(&connection, 2, 1001, Some(7000), Some(5000));
        insert_participant(&connection, 2, 1002, Some(7000), Some(6000));

        // Event C: neither has an alliance, same corp -> linked (corp fallback).
        insert_evidence(&connection, 3, "2025-03-01T00:00:00Z", "2025-03-01");
        insert_participant(&connection, 3, 1001, Some(7000), None);
        insert_participant(&connection, 3, 1002, Some(7000), None);

        // Event D: neither has an alliance, different corp -> unlinked.
        insert_evidence(&connection, 4, "2025-04-01T00:00:00Z", "2025-04-01");
        insert_participant(&connection, 4, 1001, Some(7000), None);
        insert_participant(&connection, 4, 1002, Some(7001), None);

        // Event E: one side has an alliance, the other does not, same corp -> unlinked (alliance takes precedence).
        insert_evidence(&connection, 5, "2025-05-01T00:00:00Z", "2025-05-01");
        insert_participant(&connection, 5, 1001, Some(7000), Some(5000));
        insert_participant(&connection, 5, 1002, Some(7000), None);

        // Event F: neither has an alliance nor a corp on record -> unlinked (no organisation data to link on).
        insert_evidence(&connection, 6, "2025-06-01T00:00:00Z", "2025-06-01");
        insert_participant(&connection, 6, 1001, None, None);
        insert_participant(&connection, 6, 1002, None, None);

        let stats = rebuild_summary_and_org_context(&connection).unwrap();
        assert_eq!(stats.summary_rows, 1);
        assert_eq!(stats.org_context_rows, 1);

        let (shared_event_count, first_seen_utc, last_seen_utc): (i64, String, String) = connection
            .query_row(
                "SELECT shared_event_count, first_seen_utc, last_seen_utc \
                 FROM historic_relationship_summary WHERE pilot_a_id = 1001 AND pilot_b_id = 1002;",
                [],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();

        assert_eq!(shared_event_count, 6);
        assert_eq!(first_seen_utc, "2025-01-01T00:00:00Z");
        assert_eq!(last_seen_utc, "2025-06-01T00:00:00Z");

        let (
            linked_count,
            first_seen_linked_utc,
            last_seen_linked_utc,
            unlinked_count,
            first_seen_unlinked_utc,
            last_seen_unlinked_utc,
        ): (i64, String, String, i64, String, String) = connection
            .query_row(
                "SELECT shared_event_count_linked, first_seen_linked_utc, last_seen_linked_utc, \
                        shared_event_count_unlinked, first_seen_unlinked_utc, last_seen_unlinked_utc \
                 FROM historic_relationship_org_context WHERE pilot_a_id = 1001 AND pilot_b_id = 1002;",
                [],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?, row.get(4)?, row.get(5)?)),
            )
            .unwrap();

        assert_eq!(linked_count, 2);
        assert_eq!(first_seen_linked_utc, "2025-01-01T00:00:00Z");
        assert_eq!(last_seen_linked_utc, "2025-03-01T00:00:00Z");
        assert_eq!(unlinked_count, 4);
        assert_eq!(first_seen_unlinked_utc, "2025-02-01T00:00:00Z");
        assert_eq!(last_seen_unlinked_utc, "2025-06-01T00:00:00Z");
        assert_eq!(linked_count + unlinked_count, shared_event_count);
    }

    #[test]
    fn rebuild_on_empty_database_produces_zero_rows() {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        crate::schema::insert_initial_metadata_row(&connection, "2026-08-05T00:00:00Z").unwrap();

        let stats = rebuild_summary_and_org_context(&connection).unwrap();

        assert_eq!(stats.summary_rows, 0);
        assert_eq!(stats.org_context_rows, 0);
    }
}
