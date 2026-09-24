use chrono::{DateTime, Duration, Utc};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
#[path = "kr_engine.rs"]
pub mod kr_engine;
use kr_engine::contracts::{
    GroupDetectionDiagnosticsEnvelope, GroupDetectionResponse, GroupRelationshipResponse, PilotAnalysisRequest,
    PilotAnalysisResponse, ThreatDiagnosticsEnvelope,
};
use kr_engine::group_analysis::chained_relationship_analyzer::analyze_chained_relationships;
use kr_engine::group_analysis::direct_relationship_analyzer::analyze_direct_relationships;
use kr_engine::group_analysis::group_detection_configuration::{
    load_default_group_detection_configuration, GroupDetectionConfiguration,
};
use kr_engine::group_analysis::group_detection_diagnostics::run_group_detection_diagnostics;
use kr_engine::group_analysis::group_relationship_scoring::score_and_select_relationships;
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::repositories::activity_cache_repository::ActivityCacheRepository;
use kr_engine::repositories::killmail_relationship_repository::KillmailRelationshipRepository;
use kr_engine::repositories::pilot_identity_repository::PilotIdentityRepository;
use kr_engine::repositories::recent_killmail_repository::{RecentKillmailRepository, RecentKillmailSnapshot};
use kr_engine::repositories::zkill_statistics_repository::ZKillStatisticsRepository;
use kr_engine::shared::database_path::get_database_path;
use kr_engine::shared::recent_window_configuration::{
    load_default_recent_window_configuration, RecentWindowConfiguration,
};
use kr_engine::threat_analysis::{
    analyze_intrinsic_threat, analyze_intrinsic_threat_diagnostics, load_default_threat_configuration,
    ThreatConfiguration,
};
pub use kr_engine::*;
struct RuntimeState {
    database_path: PathBuf,
    threat_configuration: ThreatConfiguration,
    recent_window_configuration: RecentWindowConfiguration,
    group_detection_configuration: GroupDetectionConfiguration,
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
        let group_detection_configuration = match load_default_group_detection_configuration() {
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
            group_detection_configuration,
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
        let Some((database_path, threat_configuration, recent_window_configuration, group_detection_configuration)) =
            get_runtime_state()
        else {
            return failure_response(request.character_id, "missing_runtime");
        };

        if let Some(scanned_character_ids) = &request.scanned_character_ids {
            return analyze_group_detection(
                request.character_id,
                scanned_character_ids,
                database_path,
                &recent_window_configuration,
                &group_detection_configuration,
            );
        }

        let recent_killmail_repository = RecentKillmailRepository::new(database_path.clone());
        let zkill_statistics_repository = ZKillStatisticsRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path.clone());
        let activity_cache_repository = ActivityCacheRepository::new(database_path);

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
        let coverage_start_text = match activity_cache_repository.get_coverage_start_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return failure_response(request.character_id, "repository_read_error:activity_cache"),
        };
        let coverage_start_utc = coverage_start_text.and_then(|text| {
            DateTime::parse_from_rfc3339(&text)
                .ok()
                .map(|value| value.with_timezone(&Utc))
        });

        let now = Utc::now();
        let recent_window_killmails = filter_recent_window(
            &all_killmails,
            recent_window_configuration.recent_window_days,
            now,
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
            coverage_start_utc,
            now,
            recent_window_configuration.recent_window_days,
        );
        response_json(PilotAnalysisResponse {
            character_id: request.character_id,
            recent_style: Some(recent_style),
            threat: Some(threat),
            group_detection: None,
            failure: None,
        })
    });
    result.unwrap_or_else(|_| failure_response(0, "internal_error"))
}

fn analyze_group_detection(
    character_id: i64,
    scanned_character_ids: &[i64],
    database_path: PathBuf,
    recent_window_configuration: &RecentWindowConfiguration,
    group_detection_configuration: &GroupDetectionConfiguration,
) -> *mut c_char {
    let killmail_relationship_repository = KillmailRelationshipRepository::new(database_path.clone());
    let pilot_identity_repository = PilotIdentityRepository::new(database_path);

    let direct_evidence = match killmail_relationship_repository.get_attacker_evidence_for_scan_set(scanned_character_ids) {
        Ok(value) => value,
        Err(_) => return failure_response(character_id, "repository_read_error:direct_evidence"),
    };
    let chain_evidence = match killmail_relationship_repository
        .get_qualifying_attacker_evidence_touching_scan_set(scanned_character_ids)
    {
        Ok(value) => value,
        Err(_) => return failure_response(character_id, "repository_read_error:chain_evidence"),
    };
    let current_identities = match pilot_identity_repository.get_for_characters(scanned_character_ids) {
        Ok(value) => value,
        Err(_) => return failure_response(character_id, "repository_read_error:identity"),
    };

    let direct_relationships = analyze_direct_relationships(
        &direct_evidence,
        &current_identities,
        group_detection_configuration.npc_corporation_id_threshold,
        group_detection_configuration.minimum_shared_events,
    );
    let chained_relationships = analyze_chained_relationships(
        &chain_evidence,
        &current_identities,
        group_detection_configuration.npc_corporation_id_threshold,
        group_detection_configuration.minimum_shared_events,
        recent_window_configuration.recent_window_days,
        Utc::now(),
    );

    let scored_relationships = score_and_select_relationships(
        &direct_relationships,
        &chained_relationships,
        group_detection_configuration,
        recent_window_configuration.recent_window_days,
    );

    let relationships: Vec<GroupRelationshipResponse> =
        scored_relationships.into_iter().map(GroupRelationshipResponse::from).collect();

    response_json(PilotAnalysisResponse {
        character_id,
        recent_style: None,
        threat: None,
        group_detection: Some(GroupDetectionResponse { relationships }),
        failure: None,
    })
}
#[no_mangle]
pub extern "C" fn pintel_diagnose_group_detection(request_json: *const c_char) -> *mut c_char {
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return group_detection_diagnostics_failure(0, "unreadable_request");
        }
        let request_text = unsafe { CStr::from_ptr(request_json) };
        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(0, "unreadable_request"),
        };
        let request = match serde_json::from_str::<PilotAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(0, "unreadable_request"),
        };
        let Some((database_path, _threat_configuration, recent_window_configuration, group_detection_configuration)) =
            get_runtime_state()
        else {
            return group_detection_diagnostics_failure(request.character_id, "missing_runtime");
        };
        let Some(scanned_character_ids) = &request.scanned_character_ids else {
            return group_detection_diagnostics_failure(request.character_id, "missing_scanned_character_ids");
        };

        let killmail_relationship_repository = KillmailRelationshipRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path);

        let direct_evidence = match killmail_relationship_repository.get_attacker_evidence_for_scan_set(scanned_character_ids) {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(request.character_id, "repository_read_error:direct_evidence"),
        };
        let chain_evidence = match killmail_relationship_repository
            .get_qualifying_attacker_evidence_touching_scan_set(scanned_character_ids)
        {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(request.character_id, "repository_read_error:chain_evidence"),
        };
        let current_identities = match pilot_identity_repository.get_for_characters(scanned_character_ids) {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(request.character_id, "repository_read_error:identity"),
        };

        let diagnostics = run_group_detection_diagnostics(
            &direct_evidence,
            &chain_evidence,
            &current_identities,
            &group_detection_configuration,
            recent_window_configuration.recent_window_days,
            Utc::now(),
        );

        group_detection_diagnostics_json(GroupDetectionDiagnosticsEnvelope {
            character_id: request.character_id,
            diagnostics: Some(diagnostics.into()),
            failure: None,
        })
    });
    result.unwrap_or_else(|_| group_detection_diagnostics_failure(0, "internal_error"))
}

#[no_mangle]
pub extern "C" fn pintel_diagnose_threat(request_json: *const c_char) -> *mut c_char {
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return threat_diagnostics_failure(0, "unreadable_request");
        }
        let request_text = unsafe { CStr::from_ptr(request_json) };
        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(0, "unreadable_request"),
        };
        let request = match serde_json::from_str::<PilotAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(0, "unreadable_request"),
        };
        let Some((database_path, threat_configuration, recent_window_configuration, _group_detection_configuration)) =
            get_runtime_state()
        else {
            return threat_diagnostics_failure(request.character_id, "missing_runtime");
        };

        let recent_killmail_repository = RecentKillmailRepository::new(database_path.clone());
        let zkill_statistics_repository = ZKillStatisticsRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path.clone());
        let activity_cache_repository = ActivityCacheRepository::new(database_path);

        let all_killmails = match recent_killmail_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(request.character_id, "repository_read_error:killmails"),
        };
        let statistics = match zkill_statistics_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(request.character_id, "repository_read_error:statistics"),
        };
        let identity = match pilot_identity_repository.get_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(request.character_id, "repository_read_error:identity"),
        };
        let coverage_start_text = match activity_cache_repository.get_coverage_start_for_character(request.character_id) {
            Ok(value) => value,
            Err(_) => return threat_diagnostics_failure(request.character_id, "repository_read_error:activity_cache"),
        };
        let coverage_start_utc = coverage_start_text.and_then(|text| {
            DateTime::parse_from_rfc3339(&text)
                .ok()
                .map(|value| value.with_timezone(&Utc))
        });

        let now = Utc::now();
        let recent_window_killmails = filter_recent_window(
            &all_killmails,
            recent_window_configuration.recent_window_days,
            now,
        );

        let diagnostics = analyze_intrinsic_threat_diagnostics(
            &threat_configuration,
            statistics.as_ref(),
            &recent_window_killmails,
            identity.as_ref(),
            coverage_start_utc,
            now,
            recent_window_configuration.recent_window_days,
        );

        threat_diagnostics_json(ThreatDiagnosticsEnvelope {
            character_id: request.character_id,
            diagnostics: Some(diagnostics.into()),
            failure: None,
        })
    });
    result.unwrap_or_else(|_| threat_diagnostics_failure(0, "internal_error"))
}

fn group_detection_diagnostics_failure(character_id: i64, reason: &str) -> *mut c_char {
    group_detection_diagnostics_json(GroupDetectionDiagnosticsEnvelope {
        character_id,
        diagnostics: None,
        failure: Some(reason.to_string()),
    })
}

fn group_detection_diagnostics_json(envelope: GroupDetectionDiagnosticsEnvelope) -> *mut c_char {
    let json = serde_json::to_string(&envelope).unwrap_or_else(|_| {
        "{\"character_id\":0,\"failure\":\"internal_error\"}".to_string()
    });
    CString::new(json).unwrap_or_else(|_| {
        CString::new("{\"character_id\":0,\"failure\":\"internal_error\"}").unwrap()
    }).into_raw()
}

fn threat_diagnostics_failure(character_id: i64, reason: &str) -> *mut c_char {
    threat_diagnostics_json(ThreatDiagnosticsEnvelope {
        character_id,
        diagnostics: None,
        failure: Some(reason.to_string()),
    })
}

fn threat_diagnostics_json(envelope: ThreatDiagnosticsEnvelope) -> *mut c_char {
    let json = serde_json::to_string(&envelope).unwrap_or_else(|_| {
        "{\"character_id\":0,\"failure\":\"internal_error\"}".to_string()
    });
    CString::new(json).unwrap_or_else(|_| {
        CString::new("{\"character_id\":0,\"failure\":\"internal_error\"}").unwrap()
    }).into_raw()
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
fn get_runtime_state(
) -> Option<(PathBuf, ThreatConfiguration, RecentWindowConfiguration, GroupDetectionConfiguration)> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some((
        runtime.database_path.clone(),
        runtime.threat_configuration.clone(),
        runtime.recent_window_configuration.clone(),
        runtime.group_detection_configuration.clone(),
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
        group_detection: None,
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
