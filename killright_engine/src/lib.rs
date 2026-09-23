use chrono::{DateTime, Duration, Utc};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
#[path = "kr_engine.rs"]
pub mod kr_engine;
use kr_engine::contracts::{PilotAnalysisRequest, PilotAnalysisResponse};
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::repositories::pilot_identity_repository::PilotIdentityRepository;
use kr_engine::repositories::recent_killmail_repository::{RecentKillmailRepository, RecentKillmailSnapshot};
use kr_engine::repositories::zkill_statistics_repository::ZKillStatisticsRepository;
use kr_engine::shared::database_path::get_database_path;
use kr_engine::shared::recent_window_configuration::{
    load_default_recent_window_configuration, RecentWindowConfiguration,
};
use kr_engine::threat_analysis::{
    analyze_intrinsic_threat, load_default_threat_configuration, ThreatConfiguration,
};
pub use kr_engine::*;
struct RuntimeState {
    database_path: PathBuf,
    threat_configuration: ThreatConfiguration,
    recent_window_configuration: RecentWindowConfiguration,
}
static RUNTIME: OnceLock<Mutex<Option<RuntimeState>>> = OnceLock::new();
#[no_mangle]
pub extern "C" fn pintel_initialize() -> i32 {
    let result = panic::catch_unwind(|| {
        let database_path = match get_database_path() {
            Ok(value) => value,
            Err(_) => return 0,
        };
        let threat_configuration = match load_default_threat_configuration() {
            Ok(value) => value,
            Err(error) => {
                eprintln!("{}", error);
                return 0;
            }
        };
        let recent_window_configuration = match load_default_recent_window_configuration() {
            Ok(value) => value,
            Err(error) => {
                eprintln!("{}", error);
                return 0;
            }
        };
        let cell = RUNTIME.get_or_init(|| Mutex::new(None));
        let mut guard = match cell.lock() {
            Ok(value) => value,
            Err(_) => return 0,
        };
        *guard = Some(RuntimeState {
            database_path,
            threat_configuration,
            recent_window_configuration,
        });
        1
    });
    result.unwrap_or(0)
}
#[no_mangle]
pub extern "C" fn pintel_analyze_pilot(request_json: *const c_char) -> *mut c_char {
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return failure_response(0, "unreadable_request");
        }
        let request_text = unsafe { CStr::from_ptr(request_json) };
        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return failure_response(0, "unreadable_request"),
        };
        let request = match serde_json::from_str::<PilotAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return failure_response(0, "unreadable_request"),
        };
        let Some((database_path, threat_configuration, recent_window_configuration)) = get_runtime_state() else {
            return failure_response(request.character_id, "missing_runtime");
        };
        let recent_killmail_repository = RecentKillmailRepository::new(database_path.clone());
        let zkill_statistics_repository = ZKillStatisticsRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path);

        let all_killmails = match recent_killmail_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return failure_response(request.character_id, "repository_read_error:killmails"),
        };
        let statistics = match zkill_statistics_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return failure_response(request.character_id, "repository_read_error:statistics"),
        };
        let identity = match pilot_identity_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return failure_response(request.character_id, "repository_read_error:identity"),
        };

        let recent_window_killmails = filter_recent_window(
            &all_killmails,
            recent_window_configuration.recent_window_days,
            Utc::now(),
        );
        let killmails = recent_window_killmails
            .iter()
            .map(|row| RecentKillmailInput {
                killmail_id: row.killmail_id,
                is_loss: row.is_loss,
                attacker_count: row.attacker_count,
                is_solo: row.is_solo,
                ship_type_id: row.ship_type_id,
            })
            .collect::<Vec<_>>();
        let recent_style = analyze_recent_style(RecentStyleRequest {
            character_id: request.character_id,
            killmails,
        })
        .recent_style;
        let threat = analyze_intrinsic_threat(
            &threat_configuration,
            statistics.as_ref(),
            &recent_window_killmails,
            identity.as_ref(),
        );
        response_json(PilotAnalysisResponse {
            character_id: request.character_id,
            recent_style: Some(recent_style),
            threat: Some(threat),
            failure: None,
        })
    });
    result.unwrap_or_else(|_| failure_response(0, "internal_error"))
}
#[no_mangle]
pub extern "C" fn pintel_shutdown() {
    let _ = panic::catch_unwind(|| {
        if let Some(cell) = RUNTIME.get() {
            if let Ok(mut guard) = cell.lock() {
                *guard = None;
            }
        }
    });
}
#[no_mangle]
pub extern "C" fn pintel_free_string(value: *mut c_char) {
    if value.is_null() {
        return;
    }
    unsafe {
        let _ = CString::from_raw(value);
    }
}
fn get_runtime_state() -> Option<(PathBuf, ThreatConfiguration, RecentWindowConfiguration)> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some((
        runtime.database_path.clone(),
        runtime.threat_configuration.clone(),
        runtime.recent_window_configuration.clone(),
    ))
}

fn filter_recent_window(
    killmails: &[RecentKillmailSnapshot],
    window_days: i64,
    now: DateTime<Utc>,
) -> Vec<RecentKillmailSnapshot> {
    let cutoff = now - Duration::days(window_days);

    killmails
        .iter()
        .filter(|row| {
            DateTime::parse_from_rfc3339(&row.kill_time_utc)
                .map(|timestamp| timestamp.with_timezone(&Utc) >= cutoff)
                .unwrap_or(false)
        })
        .cloned()
        .collect()
}
fn failure_response(character_id: i64, reason: &str) -> *mut c_char {
    response_json(PilotAnalysisResponse {
        character_id,
        recent_style: None,
        threat: None,
        failure: Some(reason.to_string()),
    })
}
fn response_json(response: PilotAnalysisResponse) -> *mut c_char {
    let json = serde_json::to_string(&response).unwrap_or_else(|_| {
        "{\"character_id\":0,\"failure\":\"internal_error\"}".to_string()
    });
    CString::new(json).unwrap_or_else(|_| {
        CString::new("{\"character_id\":0,\"failure\":\"internal_error\"}").unwrap()
    }).into_raw()
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn snapshot(killmail_id: i64, kill_time_utc: &str) -> RecentKillmailSnapshot {
        RecentKillmailSnapshot {
            killmail_id,
            killmail_hash: None,
            character_id: 95465499,
            kill_time_utc: kill_time_utc.to_string(),
            is_loss: false,
            attacker_count: 2,
            is_solo: false,
            ship_type_id: None,
            system_id: Some(30000142),
            location_id: None,
            is_npc: false,
            cached_at_utc: kill_time_utc.to_string(),
        }
    }

    #[test]
    fn filter_recent_window_keeps_killmails_inside_window() {
        let now = Utc.with_ymd_and_hms(2026, 9, 23, 0, 0, 0).unwrap();
        let killmails = vec![snapshot(1, "2026-09-20T00:00:00+00:00")];

        let filtered = filter_recent_window(&killmails, 14, now);

        assert_eq!(filtered.len(), 1);
    }

    #[test]
    fn filter_recent_window_excludes_killmails_outside_window() {
        let now = Utc.with_ymd_and_hms(2026, 9, 23, 0, 0, 0).unwrap();
        let killmails = vec![snapshot(2, "2026-08-01T00:00:00+00:00")];

        let filtered = filter_recent_window(&killmails, 14, now);

        assert!(filtered.is_empty());
    }

    #[test]
    fn filter_recent_window_respects_configured_window_length() {
        let now = Utc.with_ymd_and_hms(2026, 9, 23, 0, 0, 0).unwrap();
        let killmails = vec![snapshot(3, "2026-09-10T00:00:00+00:00")];

        assert!(filter_recent_window(&killmails, 7, now).is_empty());
        assert_eq!(filter_recent_window(&killmails, 14, now).len(), 1);
    }
}
