use std::collections::HashMap;
use std::sync::Arc;
use std::thread;

use chrono::{DateTime, NaiveDate, Utc};
use serde_json::Value;

use crate::rate_limiter::RequestRateLimiter;

const HISTORY_ENDPOINT_FORMAT: &str = "https://r2z2.zkillboard.com/history/raw/{date}.json";
const MINIMUM_QUALIFYING_ATTACKERS: usize = 2;
const FLEET_MINIMUM_ATTACKERS: usize = 11;
const POD_SHIP_TYPE_ID: i64 = 670;

#[derive(Debug, Clone)]
pub struct EvidenceRecord {
    pub killmail_id: i64,
    pub killmail_time_utc: DateTime<Utc>,
    pub evidence_date_utc: NaiveDate,
    pub solar_system_id: Option<i64>,
    pub victim_ship_type_id: Option<i64>,
    pub participant_count: i32,
}

#[derive(Debug, Clone)]
pub struct ParticipantRecord {
    /// The killmail this participant appeared on. This doubles as the future
    /// evidence_id when persisted, matching the C# convention where
    /// evidence_id is the killmail_id directly.
    pub killmail_id: i64,
    pub character_id: i64,
    pub corporation_id: Option<i64>,
    pub alliance_id: Option<i64>,
    pub ship_type_id: Option<i64>,
}

#[derive(Debug, Clone)]
pub struct DayResult {
    pub date: NaiveDate,
    pub url: String,
    pub succeeded: bool,
    pub raw_killmail_count: i32,
    pub raw_attacker_count: i32,
    pub pod_killmail_count: i32,
    pub insufficient_attacker_killmail_count: i32,
    pub fleet_killmail_count: i32,
    pub qualifying_killmail_count: i32,
    pub qualifying_attacker_count: i32,
    pub candidate_pair_occurrence_rows: i64,
    pub max_qualifying_attackers_on_killmail: i32,
    pub error_message: Option<String>,
}

#[derive(Debug, Clone)]
pub struct EvidenceDayResult {
    pub day_result: DayResult,
    pub evidence_rows: Vec<EvidenceRecord>,
    pub participant_rows: Vec<ParticipantRecord>,
}

#[derive(Debug, Clone, Copy)]
pub struct ParallelDownloadOptions {
    pub parallel_download_workers: u32,
    pub max_requests_per_second: u32,
    pub zkill_documented_max_requests_per_second: u32,
}

impl ParallelDownloadOptions {
    pub const MINIMUM_PARALLEL_DOWNLOAD_WORKERS: u32 = 1;
    pub const MAXIMUM_PARALLEL_DOWNLOAD_WORKERS: u32 = 16;
    pub const DEFAULT_PARALLEL_DOWNLOAD_WORKERS: u32 = 8;

    pub const SAFETY_MARGIN_REQUESTS_PER_SECOND: u32 = 2;
    pub const MINIMUM_DOCUMENTED_MAX_REQUESTS_PER_SECOND: u32 = Self::SAFETY_MARGIN_REQUESTS_PER_SECOND + 1;
    pub const MAXIMUM_DOCUMENTED_MAX_REQUESTS_PER_SECOND: u32 = 1000;
    pub const DEFAULT_DOCUMENTED_MAX_REQUESTS_PER_SECOND: u32 = 15;

    pub fn from_configured_values(parallel_download_workers: u32, zkill_documented_max_requests_per_second: u32) -> Self {
        let clamped_ceiling = Self::clamp_documented_max_requests_per_second(zkill_documented_max_requests_per_second);
        let effective_max_requests_per_second = clamped_ceiling - Self::SAFETY_MARGIN_REQUESTS_PER_SECOND;

        ParallelDownloadOptions {
            parallel_download_workers: parallel_download_workers.clamp(
                Self::MINIMUM_PARALLEL_DOWNLOAD_WORKERS,
                Self::MAXIMUM_PARALLEL_DOWNLOAD_WORKERS,
            ),
            max_requests_per_second: effective_max_requests_per_second,
            zkill_documented_max_requests_per_second: clamped_ceiling,
        }
    }

    pub fn clamp_documented_max_requests_per_second(value: u32) -> u32 {
        value.clamp(
            Self::MINIMUM_DOCUMENTED_MAX_REQUESTS_PER_SECOND,
            Self::MAXIMUM_DOCUMENTED_MAX_REQUESTS_PER_SECOND,
        )
    }
}

impl Default for ParallelDownloadOptions {
    fn default() -> Self {
        Self::from_configured_values(
            Self::DEFAULT_PARALLEL_DOWNLOAD_WORKERS,
            Self::DEFAULT_DOCUMENTED_MAX_REQUESTS_PER_SECOND,
        )
    }
}

pub struct ZkillHistoryClient {
    http_client: reqwest::blocking::Client,
    options: ParallelDownloadOptions,
}

impl ZkillHistoryClient {
    pub fn new(options: ParallelDownloadOptions) -> reqwest::Result<Self> {
        let http_client = reqwest::blocking::Client::builder()
            .user_agent("KillRight-HistoryUpdater/19.00.44")
            .build()?;

        Ok(ZkillHistoryClient { http_client, options })
    }

    pub fn extract_day_evidence(&self, date: NaiveDate, rate_limiter: Option<&RequestRateLimiter>) -> EvidenceDayResult {
        let date_text = date.format("%Y%m%d").to_string();
        let url = HISTORY_ENDPOINT_FORMAT.replace("{date}", &date_text);

        if let Some(limiter) = rate_limiter {
            limiter.wait();
        }

        let response = match self.http_client.get(&url).header("Accept", "application/json").send() {
            Ok(response) => response,
            Err(error) => return failed_result(date, url, error.to_string()),
        };

        if !response.status().is_success() {
            let status = response.status();
            return failed_result(
                date,
                url,
                format!("HTTP {} {}", status.as_u16(), status.canonical_reason().unwrap_or("")),
            );
        }

        let body = match response.text() {
            Ok(body) => body,
            Err(error) => return failed_result(date, url, error.to_string()),
        };

        let root: Value = match serde_json::from_str(&body) {
            Ok(value) => value,
            Err(error) => return failed_result(date, url, error.to_string()),
        };

        count_day_metrics(date, url, &root)
    }

    pub fn extract_days_evidence(&self, dates: &[NaiveDate]) -> Vec<EvidenceDayResult> {
        if dates.is_empty() {
            return Vec::new();
        }

        let mut ordered_dates = dates.to_vec();
        ordered_dates.sort();

        let rate_limiter = Arc::new(RequestRateLimiter::new(self.options.max_requests_per_second));
        let mut results: Vec<EvidenceDayResult> = Vec::with_capacity(ordered_dates.len());

        for batch in ordered_dates.chunks(self.options.parallel_download_workers as usize) {
            thread::scope(|scope| {
                let handles: Vec<_> = batch
                    .iter()
                    .map(|date| {
                        let limiter = Arc::clone(&rate_limiter);
                        let date = *date;
                        scope.spawn(move || self.extract_day_evidence(date, Some(limiter.as_ref())))
                    })
                    .collect();

                for handle in handles {
                    results.push(handle.join().expect("history download worker thread panicked"));
                }
            });
        }

        results.sort_by(|left, right| left.day_result.date.cmp(&right.day_result.date));
        results
    }
}

fn failed_result(date: NaiveDate, url: String, error_message: String) -> EvidenceDayResult {
    EvidenceDayResult {
        day_result: DayResult {
            date,
            url,
            succeeded: false,
            raw_killmail_count: 0,
            raw_attacker_count: 0,
            pod_killmail_count: 0,
            insufficient_attacker_killmail_count: 0,
            fleet_killmail_count: 0,
            qualifying_killmail_count: 0,
            qualifying_attacker_count: 0,
            candidate_pair_occurrence_rows: 0,
            max_qualifying_attackers_on_killmail: 0,
            error_message: Some(error_message),
        },
        evidence_rows: Vec::new(),
        participant_rows: Vec::new(),
    }
}

fn count_day_metrics(date: NaiveDate, url: String, root: &Value) -> EvidenceDayResult {
    let killmails = match root.as_object() {
        Some(map) => map,
        None => return failed_result(date, url, "Root JSON was not an object.".to_string()),
    };

    let mut raw_killmail_count = 0i32;
    let mut raw_attacker_count = 0i32;
    let mut pod_killmail_count = 0i32;
    let mut insufficient_attacker_killmail_count = 0i32;
    let mut fleet_killmail_count = 0i32;
    let mut qualifying_killmail_count = 0i32;
    let mut qualifying_attacker_count = 0i32;
    let mut candidate_pair_occurrence_rows = 0i64;
    let mut max_qualifying_attackers_on_killmail = 0i32;
    let mut evidence_rows = Vec::new();
    let mut participant_rows = Vec::new();

    for (key, killmail) in killmails {
        raw_killmail_count += 1;

        let killmail_object = match killmail.as_object() {
            Some(value) => value,
            None => continue,
        };

        let attackers = match killmail_object.get("attackers").and_then(Value::as_array) {
            Some(value) => value,
            None => continue,
        };

        raw_attacker_count += attackers.len() as i32;

        let victim_ship_type_id = killmail_object
            .get("victim")
            .and_then(Value::as_object)
            .and_then(|victim| victim.get("ship_type_id"))
            .and_then(Value::as_i64);

        if victim_ship_type_id == Some(POD_SHIP_TYPE_ID) {
            pod_killmail_count += 1;
            continue;
        }

        let unique_attackers = get_unique_attackers(attackers);
        let mut attacker_character_ids: Vec<i64> = unique_attackers.keys().copied().collect();
        attacker_character_ids.sort();

        if attacker_character_ids.len() < MINIMUM_QUALIFYING_ATTACKERS {
            insufficient_attacker_killmail_count += 1;
            continue;
        }

        if attacker_character_ids.len() >= FLEET_MINIMUM_ATTACKERS {
            fleet_killmail_count += 1;
            continue;
        }

        let killmail_id = killmail_object
            .get("killmail_id")
            .and_then(Value::as_i64)
            .or_else(|| key.parse::<i64>().ok())
            .unwrap_or_default();

        let killmail_time_utc = killmail_object
            .get("killmail_time")
            .and_then(Value::as_str)
            .and_then(|text| DateTime::parse_from_rfc3339(text).ok())
            .map(|value| value.with_timezone(&Utc))
            .unwrap_or_else(|| date.and_hms_opt(0, 0, 0).unwrap().and_utc());

        let solar_system_id = killmail_object.get("solar_system_id").and_then(Value::as_i64);

        qualifying_killmail_count += 1;
        qualifying_attacker_count += attacker_character_ids.len() as i32;
        candidate_pair_occurrence_rows += count_pairs(attacker_character_ids.len());
        max_qualifying_attackers_on_killmail = max_qualifying_attackers_on_killmail.max(attacker_character_ids.len() as i32);

        evidence_rows.push(EvidenceRecord {
            killmail_id,
            killmail_time_utc,
            evidence_date_utc: date,
            solar_system_id,
            victim_ship_type_id,
            participant_count: attacker_character_ids.len() as i32,
        });

        for character_id in &attacker_character_ids {
            let attacker = &unique_attackers[character_id];
            participant_rows.push(ParticipantRecord {
                killmail_id,
                character_id: *character_id,
                corporation_id: attacker.corporation_id,
                alliance_id: attacker.alliance_id,
                ship_type_id: attacker.ship_type_id,
            });
        }
    }

    EvidenceDayResult {
        day_result: DayResult {
            date,
            url,
            succeeded: true,
            raw_killmail_count,
            raw_attacker_count,
            pod_killmail_count,
            insufficient_attacker_killmail_count,
            fleet_killmail_count,
            qualifying_killmail_count,
            qualifying_attacker_count,
            candidate_pair_occurrence_rows,
            max_qualifying_attackers_on_killmail,
            error_message: None,
        },
        evidence_rows,
        participant_rows,
    }
}

struct AttackerSnapshot {
    corporation_id: Option<i64>,
    alliance_id: Option<i64>,
    ship_type_id: Option<i64>,
}

fn get_unique_attackers(attackers: &[Value]) -> HashMap<i64, AttackerSnapshot> {
    let mut values = HashMap::new();

    for attacker in attackers {
        let attacker_object = match attacker.as_object() {
            Some(value) => value,
            None => continue,
        };

        let character_id = match attacker_object.get("character_id").and_then(Value::as_i64) {
            Some(value) if value > 0 => value,
            _ => continue,
        };

        values.insert(
            character_id,
            AttackerSnapshot {
                corporation_id: attacker_object.get("corporation_id").and_then(Value::as_i64),
                alliance_id: attacker_object.get("alliance_id").and_then(Value::as_i64),
                ship_type_id: attacker_object.get("ship_type_id").and_then(Value::as_i64),
            },
        );
    }

    values
}

fn count_pairs(participant_count: usize) -> i64 {
    if participant_count < 2 {
        0
    } else {
        (participant_count as i64) * (participant_count as i64 - 1) / 2
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn qualification_rules_match_design_spec_section_4_7() {
        let mut fleet_attackers = Vec::new();

        for character_id in 9001..9012 {
            fleet_attackers.push(json!({
                "character_id": character_id,
                "corporation_id": 2001,
                "alliance_id": 3001,
                "ship_type_id": 587
            }));
        }

        let root = json!({
            "100": {
                "killmail_id": 100,
                "killmail_time": "2025-12-01T01:00:00Z",
                "solar_system_id": 30000142,
                "victim": { "ship_type_id": 670 },
                "attackers": [
                    { "character_id": 1001, "corporation_id": 2001, "alliance_id": 3001, "ship_type_id": 587 },
                    { "character_id": 1002, "corporation_id": 2002, "alliance_id": 3002, "ship_type_id": 588 }
                ]
            },
            "101": {
                "killmail_id": 101,
                "killmail_time": "2025-12-01T02:00:00Z",
                "solar_system_id": 30000142,
                "victim": { "ship_type_id": 601 },
                "attackers": [
                    { "character_id": 1003, "corporation_id": 2003, "alliance_id": 3003, "ship_type_id": 589 }
                ]
            },
            "102": {
                "killmail_id": 102,
                "killmail_time": "2025-12-01T03:00:00Z",
                "solar_system_id": 30000142,
                "victim": { "ship_type_id": 602 },
                "attackers": fleet_attackers
            },
            "103": {
                "killmail_id": 103,
                "killmail_time": "2025-12-01T04:00:00Z",
                "solar_system_id": 30000144,
                "victim": { "ship_type_id": 590 },
                "attackers": [
                    { "character_id": 1004, "corporation_id": 2004, "alliance_id": 3004, "ship_type_id": 591 },
                    { "character_id": 1005, "corporation_id": 2004, "alliance_id": 3004, "ship_type_id": 592 },
                    { "character_id": 1004, "corporation_id": 2004, "alliance_id": 3004, "ship_type_id": 591 }
                ]
            }
        });

        let date = NaiveDate::from_ymd_opt(2025, 12, 1).unwrap();
        let result = count_day_metrics(date, "https://example.invalid/test".to_string(), &root);

        assert_eq!(result.day_result.raw_killmail_count, 4);
        assert_eq!(result.day_result.raw_attacker_count, 17);
        assert_eq!(result.day_result.pod_killmail_count, 1);
        assert_eq!(result.day_result.insufficient_attacker_killmail_count, 1);
        assert_eq!(result.day_result.fleet_killmail_count, 1);
        assert_eq!(result.day_result.qualifying_killmail_count, 1);
        assert_eq!(result.day_result.qualifying_attacker_count, 2);
        assert_eq!(result.day_result.candidate_pair_occurrence_rows, 1);
        assert_eq!(result.day_result.max_qualifying_attackers_on_killmail, 2);
        assert_eq!(result.evidence_rows.len(), 1);

        let evidence_row = &result.evidence_rows[0];
        assert_eq!(evidence_row.killmail_id, 103);
        assert_eq!(evidence_row.victim_ship_type_id, Some(590));
        assert_eq!(evidence_row.participant_count, 2);

        assert_eq!(result.participant_rows.len(), 2);
        assert!(result.participant_rows.iter().any(|row| row.character_id == 1004));
        assert!(result.participant_rows.iter().any(|row| row.character_id == 1005));
    }
}
