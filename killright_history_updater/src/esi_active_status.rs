use std::time::Duration;

use duckdb::{params, Connection, Result as DuckResult};

/// ESI's compatibility-date versioning scheme (introduced before this step):
/// requests must declare which dated API contract they expect via the
/// X-Compatibility-Date header. The corporation `state` field this module
/// depends on (Design Specification Section 6.11.4/5.1) is only present in
/// the response when the header is set to a date on or after 21 Jul 2026 --
/// confirmed against the live ESI API Explorer, 13 Aug 2026. Without the
/// header the field is simply absent, not a different value. A fixed date
/// on or after that cutoff is used rather than "today", since an ESI
/// compatibility date opts into a fixed dated contract, not a moving one.
pub const ESI_COMPATIBILITY_DATE: &str = "2026-08-04";

const ALLIANCE_ENDPOINT_FORMAT: &str = "https://esi.evetech.net/alliances/{alliance_id}";
const CORPORATION_ENDPOINT_FORMAT: &str = "https://esi.evetech.net/corporations/{corporation_id}";

/// Corporation or alliance, matching historic_closed_entity_cache's
/// entity_type column (Section 4.7): 'C' or 'A'.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EntityType {
    Corporation,
    Alliance,
}

impl EntityType {
    pub fn as_cache_char(self) -> &'static str {
        match self {
            EntityType::Corporation => "C",
            EntityType::Alliance => "A",
        }
    }

    pub fn as_label(self) -> &'static str {
        match self {
            EntityType::Corporation => "Corporation",
            EntityType::Alliance => "Alliance",
        }
    }
}

pub struct EsiActiveStatusClient {
    http_client: reqwest::blocking::Client,
}

impl EsiActiveStatusClient {
    /// Matches ZkillHistoryClient's own connect-timeout reasoning
    /// (r2_client.rs): reqwest::blocking::Client has no default connect
    /// timeout at all. ESI is normally fast; this is a hard ceiling against
    /// a network-level stall, not a tuned expectation of ESI's own latency.
    pub const CONNECT_TIMEOUT_SECONDS: u64 = 10;

    /// Matches ZkillHistoryClient's own request-timeout reasoning
    /// (r2_client.rs). A single ESI corporation/alliance lookup is a small,
    /// single-object response -- far smaller than a daily killmail history
    /// file -- so this is set well below ZkillHistoryClient's 60 seconds.
    pub const REQUEST_TIMEOUT_SECONDS: u64 = 20;

    pub fn new() -> reqwest::Result<Self> {
        let http_client = reqwest::blocking::Client::builder()
            .user_agent("KillRight-HistoryUpdater/19.01.02")
            .connect_timeout(Duration::from_secs(Self::CONNECT_TIMEOUT_SECONDS))
            .timeout(Duration::from_secs(Self::REQUEST_TIMEOUT_SECONDS))
            .build()?;

        Ok(EsiActiveStatusClient { http_client })
    }

    /// Alliance status: ESI GET /alliances/{alliance_id} -- 200 OK is
    /// active, 404 is disbanded (Design Specification Section 6.11.4). No
    /// special request header needed.
    pub fn check_alliance_active(&self, alliance_id: i64) -> Result<bool, String> {
        let url = ALLIANCE_ENDPOINT_FORMAT.replace("{alliance_id}", &alliance_id.to_string());

        let response = self
            .http_client
            .get(&url)
            .header("Accept", "application/json")
            .send()
            .map_err(|error| error.to_string())?;

        interpret_alliance_status(response.status())
    }

    /// Corporation status: ESI GET /corporations/{corporation_id}, with the
    /// X-Compatibility-Date request header (Design Specification Section
    /// 6.11.4/5.1) -- required for the response to include the state field
    /// at all. The response's state field is "active" or "closed".
    pub fn check_corporation_active(&self, corporation_id: i64) -> Result<bool, String> {
        let url = CORPORATION_ENDPOINT_FORMAT.replace("{corporation_id}", &corporation_id.to_string());

        let response = self
            .http_client
            .get(&url)
            .header("Accept", "application/json")
            .header("X-Compatibility-Date", ESI_COMPATIBILITY_DATE)
            .send()
            .map_err(|error| error.to_string())?;

        let status = response.status();

        if status.as_u16() == 404 {
            return Ok(false);
        }

        let body = response.text().map_err(|error| error.to_string())?;
        interpret_corporation_state_body(status, &body)
    }
}

/// Pure decision logic for the alliance check, split out from the HTTP call
/// itself so it can be unit-tested without a real network call (matching
/// r2_client.rs's count_day_metrics/extract_day_evidence split).
fn interpret_alliance_status(status: reqwest::StatusCode) -> Result<bool, String> {
    if status.as_u16() == 200 {
        Ok(true)
    } else if status.as_u16() == 404 {
        Ok(false)
    } else {
        Err(format!(
            "Unexpected HTTP status from alliance active-status check: {} {}",
            status.as_u16(),
            status.canonical_reason().unwrap_or("")
        ))
    }
}

/// Pure decision logic for the corporation check's 200-OK body, split out
/// from the HTTP call itself so it can be unit-tested without a real
/// network call. The 404 case is handled by the caller before this is
/// reached (see check_corporation_active) since a 404 body has no state
/// field to parse.
fn interpret_corporation_state_body(status: reqwest::StatusCode, body: &str) -> Result<bool, String> {
    if status.as_u16() != 200 {
        return Err(format!(
            "Unexpected HTTP status from corporation active-status check: {} {}",
            status.as_u16(),
            status.canonical_reason().unwrap_or("")
        ));
    }

    let root: serde_json::Value = serde_json::from_str(body).map_err(|error| error.to_string())?;

    match root.get("state").and_then(serde_json::Value::as_str) {
        Some("active") => Ok(true),
        Some("closed") => Ok(false),
        Some(other) => Err(format!("Unrecognised corporation state value: \"{other}\"")),
        None => Err(
            "Corporation active-status response has no state field -- confirm the X-Compatibility-Date header is set to a date on or after 21 Jul 2026.".to_string(),
        ),
    }
}

/// True when entity_id/entity_type is already recorded in
/// historic_closed_entity_cache (Section 4.7) -- checked before ever
/// calling ESI (Section 6.11.4), since the cache is safe as an
/// append-only, no-expiry source of truth: a disbanded EVE corporation or
/// alliance ID is never reactivated under the same ID.
fn is_entity_cached_closed(connection: &Connection, entity_id: i64, entity_type: EntityType) -> DuckResult<bool> {
    let count: i64 = connection.query_row(
        "SELECT COUNT(*) FROM historic_closed_entity_cache WHERE entity_id = ? AND entity_type = ?;",
        params![entity_id, entity_type.as_cache_char()],
        |row| row.get(0),
    )?;

    Ok(count > 0)
}

/// Records entity_id/entity_type as newly discovered closed. Append-only,
/// no expiry (Section 4.7) -- this is only ever called once per entity,
/// since is_entity_cached_closed short-circuits every later check.
fn record_entity_closed(connection: &Connection, entity_id: i64, entity_type: EntityType, discovered_closed_utc: &str) -> DuckResult<()> {
    connection.execute(
        "INSERT INTO historic_closed_entity_cache (entity_id, entity_type, discovered_closed_utc) VALUES (?, ?, ?);",
        params![entity_id, entity_type.as_cache_char(), discovered_closed_utc],
    )?;

    Ok(())
}

/// The check-and-record logic itself (Design Specification Section 6.11.4,
/// Implementation Plan Step 19.01.02): checks historic_closed_entity_cache
/// first, only calls ESI (via check_active) if the entity isn't already
/// known-closed, and records a newly-discovered closure. check_active is
/// injected rather than called directly so this function's cache-first
/// logic is unit-testable without a real network call -- production
/// callers (ensure_alliance_active_status_cached/
/// ensure_corporation_active_status_cached below) pass a closure that
/// calls the real ESI endpoint.
///
/// The purge-on-closure behaviour Section 6.11.4 also describes (purging
/// any already-stored Pilot-vs-Group row for a newly-closed entity) is
/// deferred to Step 19.01.08, since no historic_relationship_classification
/// rows exist yet at this point in the sequence -- see the Implementation
/// Plan.
pub fn ensure_entity_active_status_cached<F>(
    connection: &Connection,
    entity_id: i64,
    entity_type: EntityType,
    now_utc: &str,
    check_active: F,
) -> Result<bool, String>
where
    F: FnOnce(i64, EntityType) -> Result<bool, String>,
{
    if is_entity_cached_closed(connection, entity_id, entity_type)
        .map_err(|error| format!("Failed to check historic_closed_entity_cache: {error}"))?
    {
        return Ok(false);
    }

    let is_active = check_active(entity_id, entity_type)?;

    if !is_active {
        record_entity_closed(connection, entity_id, entity_type, now_utc)
            .map_err(|error| format!("Failed to record newly-closed entity in historic_closed_entity_cache: {error}"))?;
    }

    Ok(is_active)
}

/// Production entry point for an alliance: checks the cache first, then
/// calls the real ESI alliance endpoint if not already known-closed.
pub fn ensure_alliance_active_status_cached(
    connection: &Connection,
    esi_client: &EsiActiveStatusClient,
    alliance_id: i64,
    now_utc: &str,
) -> Result<bool, String> {
    ensure_entity_active_status_cached(connection, alliance_id, EntityType::Alliance, now_utc, |id, _entity_type| {
        esi_client.check_alliance_active(id)
    })
}

/// Production entry point for a corporation: checks the cache first, then
/// calls the real ESI corporation endpoint if not already known-closed.
pub fn ensure_corporation_active_status_cached(
    connection: &Connection,
    esi_client: &EsiActiveStatusClient,
    corporation_id: i64,
    now_utc: &str,
) -> Result<bool, String> {
    ensure_entity_active_status_cached(connection, corporation_id, EntityType::Corporation, now_utc, |id, _entity_type| {
        esi_client.check_corporation_active(id)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn open_test_schema() -> Connection {
        let connection = Connection::open_in_memory().unwrap();
        crate::schema::create_schema(&connection).unwrap();
        connection
    }

    #[test]
    fn interpret_alliance_status_200_is_active() {
        assert_eq!(interpret_alliance_status(reqwest::StatusCode::OK), Ok(true));
    }

    #[test]
    fn interpret_alliance_status_404_is_disbanded() {
        assert_eq!(interpret_alliance_status(reqwest::StatusCode::NOT_FOUND), Ok(false));
    }

    #[test]
    fn interpret_alliance_status_other_status_is_an_error() {
        assert!(interpret_alliance_status(reqwest::StatusCode::INTERNAL_SERVER_ERROR).is_err());
    }

    #[test]
    fn interpret_corporation_state_body_active_is_active() {
        let body = r#"{"name":"Test Corp","ticker":"TEST","member_count":50,"state":"active"}"#;
        assert_eq!(interpret_corporation_state_body(reqwest::StatusCode::OK, body), Ok(true));
    }

    #[test]
    fn interpret_corporation_state_body_closed_is_closed() {
        let body = r#"{"name":"Test Corp","ticker":"TEST","member_count":0,"state":"closed"}"#;
        assert_eq!(interpret_corporation_state_body(reqwest::StatusCode::OK, body), Ok(false));
    }

    #[test]
    fn interpret_corporation_state_body_missing_state_field_is_an_error() {
        let body = r#"{"name":"Test Corp","ticker":"TEST","member_count":50}"#;
        let result = interpret_corporation_state_body(reqwest::StatusCode::OK, body);
        assert!(result.is_err());
        assert!(result.unwrap_err().contains("X-Compatibility-Date"));
    }

    #[test]
    fn interpret_corporation_state_body_unrecognised_state_value_is_an_error() {
        let body = r#"{"name":"Test Corp","ticker":"TEST","member_count":50,"state":"suspended"}"#;
        assert!(interpret_corporation_state_body(reqwest::StatusCode::OK, body).is_err());
    }

    #[test]
    fn interpret_corporation_state_body_non_200_status_is_an_error() {
        assert!(interpret_corporation_state_body(reqwest::StatusCode::INTERNAL_SERVER_ERROR, "").is_err());
    }

    #[test]
    fn ensure_entity_active_status_cached_returns_true_without_recording_anything_when_check_reports_active() {
        let connection = open_test_schema();

        let result = ensure_entity_active_status_cached(&connection, 95465499, EntityType::Corporation, "2026-08-13T00:00:00Z", |_, _| {
            Ok(true)
        });

        assert_eq!(result, Ok(true));
        assert!(!is_entity_cached_closed(&connection, 95465499, EntityType::Corporation).unwrap());
    }

    #[test]
    fn ensure_entity_active_status_cached_records_a_newly_discovered_closure() {
        let connection = open_test_schema();

        let result = ensure_entity_active_status_cached(&connection, 98765432, EntityType::Alliance, "2026-08-13T00:00:00Z", |_, _| {
            Ok(false)
        });

        assert_eq!(result, Ok(false));
        assert!(is_entity_cached_closed(&connection, 98765432, EntityType::Alliance).unwrap());

        let discovered_closed_utc: String = connection
            .query_row(
                "SELECT discovered_closed_utc FROM historic_closed_entity_cache WHERE entity_id = 98765432 AND entity_type = 'A';",
                [],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(discovered_closed_utc, "2026-08-13T00:00:00Z");
    }

    #[test]
    fn ensure_entity_active_status_cached_short_circuits_when_already_cached_closed_and_never_calls_check_active() {
        let connection = open_test_schema();
        record_entity_closed(&connection, 99005338, EntityType::Corporation, "2026-08-01T00:00:00Z").unwrap();

        let result = ensure_entity_active_status_cached(&connection, 99005338, EntityType::Corporation, "2026-08-13T00:00:00Z", |_, _| {
            panic!("check_active must not be called for an already-cached-closed entity");
        });

        assert_eq!(result, Ok(false));
    }

    #[test]
    fn ensure_entity_active_status_cached_propagates_a_check_active_error_without_recording_anything() {
        let connection = open_test_schema();

        let result = ensure_entity_active_status_cached(&connection, 90379338, EntityType::Alliance, "2026-08-13T00:00:00Z", |_, _| {
            Err("simulated ESI failure".to_string())
        });

        assert!(result.is_err());
        assert!(!is_entity_cached_closed(&connection, 90379338, EntityType::Alliance).unwrap());
    }

    #[test]
    fn entity_type_as_cache_char_matches_schema_convention() {
        assert_eq!(EntityType::Corporation.as_cache_char(), "C");
        assert_eq!(EntityType::Alliance.as_cache_char(), "A");
    }
}
