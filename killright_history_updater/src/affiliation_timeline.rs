use std::collections::HashMap;

use chrono::{DateTime, Utc};
use duckdb::{params, Connection, OptionalExt, Result};

use crate::r2_client::{EvidenceRecord, ParticipantRecord};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PilotDayAffiliationFragment {
    pub character_id: i64,
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub first_seen_utc: DateTime<Utc>,
    pub last_seen_utc: DateTime<Utc>,
}

pub fn build_daily_affiliation_fragments(
    evidence_rows: &[EvidenceRecord],
    participant_rows: &[ParticipantRecord],
) -> Vec<PilotDayAffiliationFragment> {
    let mut killmail_times: HashMap<i64, DateTime<Utc>> = HashMap::new();

    for evidence_row in evidence_rows {
        killmail_times.insert(evidence_row.killmail_id, evidence_row.killmail_time_utc);
    }

    let mut observations: Vec<(i64, Option<i64>, Option<i64>, DateTime<Utc>)> = Vec::new();

    for participant_row in participant_rows {
        if let Some(&observed_utc) = killmail_times.get(&participant_row.killmail_id) {
            observations.push((
                participant_row.character_id,
                participant_row.corporation_id,
                participant_row.alliance_id,
                observed_utc,
            ));
        }
    }

    observations.sort_by(|left, right| left.0.cmp(&right.0).then(left.3.cmp(&right.3)));

    let mut fragments: Vec<PilotDayAffiliationFragment> = Vec::new();

    for (character_id, corporation_id, alliance_id, observed_utc) in observations {
        match fragments.last_mut() {
            Some(current)
                if current.character_id == character_id
                    && current.corporation_id == corporation_id
                    && current.alliance_id == alliance_id =>
            {
                if observed_utc > current.last_seen_utc {
                    current.last_seen_utc = observed_utc;
                }
            }
            _ => {
                fragments.push(PilotDayAffiliationFragment {
                    character_id,
                    corporation_id,
                    alliance_id,
                    first_seen_utc: observed_utc,
                    last_seen_utc: observed_utc,
                });
            }
        }
    }

    fragments
}

struct OpenAffiliationRow {
    corporation_id: Option<i64>,
    alliance_id: Option<i64>,
    first_seen_utc: String,
}

pub fn apply_daily_affiliation_fragments(connection: &Connection, fragments: &[PilotDayAffiliationFragment], now_utc: &str) -> Result<usize> {
    let mut touched_pilot_count = 0usize;
    let mut index = 0usize;

    while index < fragments.len() {
        let character_id = fragments[index].character_id;
        let mut current = fetch_latest_affiliation_row(connection, character_id)?;

        while index < fragments.len() && fragments[index].character_id == character_id {
            let fragment = &fragments[index];

            let extends_current = match &current {
                Some(row) => row.corporation_id == fragment.corporation_id && row.alliance_id == fragment.alliance_id,
                None => false,
            };

            if extends_current {
                let first_seen_utc = current
                    .as_ref()
                    .expect("extends_current is only true when current is Some")
                    .first_seen_utc
                    .clone();

                extend_affiliation_row(connection, character_id, &first_seen_utc, &fragment.last_seen_utc.to_rfc3339(), now_utc)?;

                current = Some(OpenAffiliationRow {
                    corporation_id: fragment.corporation_id,
                    alliance_id: fragment.alliance_id,
                    first_seen_utc,
                });
            } else {
                insert_affiliation_row(connection, character_id, fragment, now_utc)?;

                current = Some(OpenAffiliationRow {
                    corporation_id: fragment.corporation_id,
                    alliance_id: fragment.alliance_id,
                    first_seen_utc: fragment.first_seen_utc.to_rfc3339(),
                });
            }

            index += 1;
        }

        touched_pilot_count += 1;
    }

    Ok(touched_pilot_count)
}

fn fetch_latest_affiliation_row(connection: &Connection, character_id: i64) -> Result<Option<OpenAffiliationRow>> {
    connection
        .query_row(
            "SELECT corporation_id, alliance_id, first_seen_utc \
             FROM historic_pilot_affiliation_timeline \
             WHERE pilot_id = ? \
             ORDER BY last_seen_utc DESC \
             LIMIT 1;",
            params![character_id],
            |row| {
                Ok(OpenAffiliationRow {
                    corporation_id: row.get(0)?,
                    alliance_id: row.get(1)?,
                    first_seen_utc: row.get(2)?,
                })
            },
        )
        .optional()
}

fn extend_affiliation_row(connection: &Connection, character_id: i64, first_seen_utc: &str, fragment_last_seen_utc: &str, now_utc: &str) -> Result<()> {
    connection.execute(
        "UPDATE historic_pilot_affiliation_timeline \
         SET last_seen_utc = GREATEST(last_seen_utc, ?), last_updated_utc = ? \
         WHERE pilot_id = ? AND first_seen_utc = ?;",
        params![fragment_last_seen_utc, now_utc, character_id, first_seen_utc],
    )?;

    Ok(())
}

fn insert_affiliation_row(connection: &Connection, character_id: i64, fragment: &PilotDayAffiliationFragment, now_utc: &str) -> Result<()> {
    connection.execute(
        "INSERT INTO historic_pilot_affiliation_timeline \
         (pilot_id, corporation_id, alliance_id, first_seen_utc, last_seen_utc, last_updated_utc) \
         VALUES (?, ?, ?, ?, ?, ?);",
        params![
            character_id,
            fragment.corporation_id,
            fragment.alliance_id,
            fragment.first_seen_utc.to_rfc3339(),
            fragment.last_seen_utc.to_rfc3339(),
            now_utc,
        ],
    )?;

    Ok(())
}

#[cfg(test)]
mod tests {
    use chrono::TimeZone;

    use super::*;

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    fn evidence(killmail_id: i64, killmail_time_utc: DateTime<Utc>) -> EvidenceRecord {
        EvidenceRecord {
            killmail_id,
            killmail_time_utc,
            evidence_date_utc: killmail_time_utc.date_naive(),
            solar_system_id: Some(30000142),
            victim_ship_type_id: Some(670),
            participant_count: 2,
        }
    }

    fn participant(killmail_id: i64, character_id: i64, corporation_id: Option<i64>, alliance_id: Option<i64>) -> ParticipantRecord {
        ParticipantRecord {
            killmail_id,
            character_id,
            corporation_id,
            alliance_id,
            ship_type_id: Some(587),
        }
    }

    fn utc(seconds_of_day: u32) -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 8, 13, 0, 0, 0).unwrap() + chrono::Duration::seconds(seconds_of_day as i64)
    }

    #[test]
    fn build_daily_affiliation_fragments_single_observation_is_one_fragment() {
        let evidence_rows = vec![evidence(1, utc(100))];
        let participant_rows = vec![participant(1, 95465499, Some(98765432), None)];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 1);
        assert_eq!(fragments[0].character_id, 95465499);
        assert_eq!(fragments[0].corporation_id, Some(98765432));
        assert_eq!(fragments[0].alliance_id, None);
        assert_eq!(fragments[0].first_seen_utc, utc(100));
        assert_eq!(fragments[0].last_seen_utc, utc(100));
    }

    #[test]
    fn build_daily_affiliation_fragments_collapses_same_affiliation_observations_out_of_input_order() {
        let evidence_rows = vec![evidence(1, utc(300)), evidence(2, utc(100)), evidence(3, utc(200))];
        let participant_rows = vec![
            participant(1, 95465499, Some(98765432), None),
            participant(2, 95465499, Some(98765432), None),
            participant(3, 95465499, Some(98765432), None),
        ];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 1);
        assert_eq!(fragments[0].first_seen_utc, utc(100));
        assert_eq!(fragments[0].last_seen_utc, utc(300));
    }

    #[test]
    fn build_daily_affiliation_fragments_splits_on_a_corporation_change() {
        let evidence_rows = vec![evidence(1, utc(100)), evidence(2, utc(200))];
        let participant_rows = vec![
            participant(1, 95465499, Some(98765432), None),
            participant(2, 95465499, Some(90379338), None),
        ];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 2);
        assert_eq!(fragments[0].corporation_id, Some(98765432));
        assert_eq!(fragments[0].first_seen_utc, utc(100));
        assert_eq!(fragments[0].last_seen_utc, utc(100));
        assert_eq!(fragments[1].corporation_id, Some(90379338));
        assert_eq!(fragments[1].first_seen_utc, utc(200));
    }

    #[test]
    fn build_daily_affiliation_fragments_splits_on_an_alliance_change_with_the_same_corporation() {
        let evidence_rows = vec![evidence(1, utc(100)), evidence(2, utc(200))];
        let participant_rows = vec![
            participant(1, 95465499, Some(98765432), Some(99005338)),
            participant(2, 95465499, Some(98765432), None),
        ];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 2);
        assert_eq!(fragments[0].alliance_id, Some(99005338));
        assert_eq!(fragments[1].alliance_id, None);
    }

    #[test]
    fn build_daily_affiliation_fragments_groups_two_pilots_separately_in_character_id_order() {
        let evidence_rows = vec![evidence(1, utc(100)), evidence(2, utc(100))];
        let participant_rows = vec![
            participant(1, 90379338, Some(98765432), None),
            participant(2, 95465499, Some(98765432), None),
        ];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 2);
        assert_eq!(fragments[0].character_id, 90379338);
        assert_eq!(fragments[1].character_id, 95465499);
    }

    #[test]
    fn build_daily_affiliation_fragments_skips_a_participant_row_with_no_matching_evidence_row() {
        let evidence_rows = vec![evidence(1, utc(100))];
        let participant_rows = vec![
            participant(1, 95465499, Some(98765432), None),
            participant(2, 95465499, Some(90379338), None),
        ];

        let fragments = build_daily_affiliation_fragments(&evidence_rows, &participant_rows);

        assert_eq!(fragments.len(), 1);
        assert_eq!(fragments[0].corporation_id, Some(98765432));
    }

    #[test]
    fn apply_daily_affiliation_fragments_inserts_a_new_row_when_none_exists() {
        let connection = open_test_schema();
        let fragments = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(98765432),
            alliance_id: None,
            first_seen_utc: utc(100),
            last_seen_utc: utc(200),
        }];

        let touched = apply_daily_affiliation_fragments(&connection, &fragments, "2026-08-13T00:00:00Z").unwrap();

        assert_eq!(touched, 1);

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 1);
    }

    #[test]
    fn apply_daily_affiliation_fragments_extends_a_matching_open_row() {
        let connection = open_test_schema();
        let day_one = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(98765432),
            alliance_id: None,
            first_seen_utc: utc(100),
            last_seen_utc: utc(200),
        }];
        apply_daily_affiliation_fragments(&connection, &day_one, "2026-08-13T00:00:00Z").unwrap();

        let day_two_last_seen_utc = utc(100) + chrono::Duration::days(1);
        let day_two = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(98765432),
            alliance_id: None,
            first_seen_utc: day_two_last_seen_utc,
            last_seen_utc: day_two_last_seen_utc,
        }];
        let touched = apply_daily_affiliation_fragments(&connection, &day_two, "2026-08-14T00:00:00Z").unwrap();

        assert_eq!(touched, 1);

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 1, "a matching fragment must extend, not open a second row");

        let last_seen_utc: String = connection
            .query_row("SELECT last_seen_utc FROM historic_pilot_affiliation_timeline WHERE pilot_id = 95465499;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(last_seen_utc, day_two_last_seen_utc.to_rfc3339());
    }

    #[test]
    fn apply_daily_affiliation_fragments_opens_a_new_row_on_a_differing_fragment() {
        let connection = open_test_schema();
        let day_one = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(98765432),
            alliance_id: None,
            first_seen_utc: utc(100),
            last_seen_utc: utc(200),
        }];
        apply_daily_affiliation_fragments(&connection, &day_one, "2026-08-13T00:00:00Z").unwrap();

        let day_two_observed_utc = utc(100) + chrono::Duration::days(1);
        let day_two = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(90379338),
            alliance_id: None,
            first_seen_utc: day_two_observed_utc,
            last_seen_utc: day_two_observed_utc,
        }];
        apply_daily_affiliation_fragments(&connection, &day_two, "2026-08-14T00:00:00Z").unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 2, "a differing fragment must open a second row, leaving the first closed");

        let first_row_last_seen_utc: String = connection
            .query_row(
                "SELECT last_seen_utc FROM historic_pilot_affiliation_timeline WHERE corporation_id = 98765432;",
                [],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(first_row_last_seen_utc, utc(200).to_rfc3339(), "the first (now closed) row must not be touched by the second insert");
    }

    #[test]
    fn apply_daily_affiliation_fragments_handles_a_same_day_corporation_change_for_one_pilot() {
        let connection = open_test_schema();
        let fragments = vec![
            PilotDayAffiliationFragment {
                character_id: 95465499,
                corporation_id: Some(98765432),
                alliance_id: None,
                first_seen_utc: utc(100),
                last_seen_utc: utc(100),
            },
            PilotDayAffiliationFragment {
                character_id: 95465499,
                corporation_id: Some(90379338),
                alliance_id: None,
                first_seen_utc: utc(200),
                last_seen_utc: utc(200),
            },
        ];

        let touched = apply_daily_affiliation_fragments(&connection, &fragments, "2026-08-13T00:00:00Z").unwrap();

        assert_eq!(touched, 1, "one pilot touched, even though two rows were opened");

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 2);
    }

    #[test]
    fn apply_daily_affiliation_fragments_counts_two_distinct_pilots_touched() {
        let connection = open_test_schema();
        let fragments = vec![
            PilotDayAffiliationFragment {
                character_id: 90379338,
                corporation_id: Some(98765432),
                alliance_id: None,
                first_seen_utc: utc(100),
                last_seen_utc: utc(100),
            },
            PilotDayAffiliationFragment {
                character_id: 95465499,
                corporation_id: Some(98765432),
                alliance_id: None,
                first_seen_utc: utc(100),
                last_seen_utc: utc(100),
            },
        ];

        let touched = apply_daily_affiliation_fragments(&connection, &fragments, "2026-08-13T00:00:00Z").unwrap();

        assert_eq!(touched, 2);
    }

    #[test]
    fn apply_daily_affiliation_fragments_never_moves_last_seen_utc_backwards_on_a_repeated_call() {
        let connection = open_test_schema();
        let fragments = vec![PilotDayAffiliationFragment {
            character_id: 95465499,
            corporation_id: Some(98765432),
            alliance_id: None,
            first_seen_utc: utc(100),
            last_seen_utc: utc(200),
        }];

        apply_daily_affiliation_fragments(&connection, &fragments, "2026-08-13T00:00:00Z").unwrap();
        apply_daily_affiliation_fragments(&connection, &fragments, "2026-08-13T00:05:00Z").unwrap();

        let row_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM historic_pilot_affiliation_timeline;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(row_count, 1);

        let last_seen_utc: String = connection
            .query_row("SELECT last_seen_utc FROM historic_pilot_affiliation_timeline WHERE pilot_id = 95465499;", [], |row| row.get(0))
            .unwrap();
        assert_eq!(last_seen_utc, utc(200).to_rfc3339());
    }
}
