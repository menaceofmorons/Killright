use chrono::{DateTime, Duration, Utc};
use std::collections::{HashMap, HashSet};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::sync::{Mutex, OnceLock};
use std::time::Instant;
#[path = "kr_engine.rs"]
pub mod kr_engine;
use kr_engine::activity_analysis::derive_activity;
use kr_engine::contracts::{
    DerivedActivityResponse, GroupDetectionDiagnosticsEnvelope, GroupDetectionResponse, GroupInputs,
    GroupRelationshipResponse, PilotAnalysisRequest, PilotAnalysisResponse, PilotIdentitySnapshot, PilotInputs,
    PilotsAnalysisRequest, PilotsAnalysisResponse, RecentKillmailSnapshot, ThreatDiagnosticsEnvelope,
    ZKillStatisticsSnapshot,
};
use kr_engine::group_analysis::chained_relationship_analyzer::analyze_chained_relationships;
use kr_engine::group_analysis::direct_relationship_analyzer::analyze_direct_relationships;
use kr_engine::group_analysis::group_detection_configuration::GroupDetectionConfiguration;
use kr_engine::group_analysis::group_detection_diagnostics::run_group_detection_diagnostics;
use kr_engine::group_analysis::group_relationship_scoring::score_and_select_relationships;
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::shared::recent_window_configuration::RecentWindowConfiguration;
use kr_engine::shared::settings_loader::load_engine_settings;
use kr_engine::shared::style_configuration::StyleConfiguration;
use kr_engine::shared::timing_recorder;
use kr_engine::threat_analysis::{analyze_intrinsic_threat, analyze_intrinsic_threat_diagnostics, ThreatConfiguration};
pub use kr_engine::*;
struct RuntimeState {
    threat_configuration: ThreatConfiguration,
    recent_window_configuration: RecentWindowConfiguration,
    group_detection_configuration: GroupDetectionConfiguration,
    style_configuration: StyleConfiguration,
}
static RUNTIME: OnceLock<Mutex<Option<RuntimeState>>> = OnceLock::new();
#[no_mangle]
pub extern "C" fn killright_initialize() -> i32 {
    let result = panic::catch_unwind(|| {
        let engine_settings = match load_engine_settings() {
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
        timing_recorder::set_enabled(engine_settings.timing.enabled);
        *guard = Some(RuntimeState {
            threat_configuration: engine_settings.threat,
            recent_window_configuration: engine_settings.recent_window,
            group_detection_configuration: engine_settings.group_detection,
            style_configuration: engine_settings.style,
        });
        1
    });
    result.unwrap_or(0)
}
#[no_mangle]
pub extern "C" fn killright_analyze_pilot(request_json: *const c_char) -> *mut c_char {
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
        let timing_started = timing_recorder::begin();
        let Some((
            threat_configuration,
            recent_window_configuration,
            group_detection_configuration,
            style_configuration,
        )) = get_runtime_state()
        else {
            return failure_response(request.character_id, "missing_runtime");
        };
        let total_phase = if request.scanned_character_ids.is_some() {
            "group_total"
        } else {
            "pilot_total"
        };
        let response = respond_to_pilot_request(
            request,
            &recent_window_configuration,
            &group_detection_configuration,
            &style_configuration,
            &threat_configuration,
        );
        if let Some(reason) = response.failure.clone() {
            return failure_response(response.character_id, &reason);
        }
        timed_response_json(response, timing_started, total_phase)
    });
    result.unwrap_or_else(|_| failure_response(0, "internal_error"))
}

fn respond_to_pilot_request(
    request: PilotAnalysisRequest,
    recent_window_configuration: &RecentWindowConfiguration,
    group_detection_configuration: &GroupDetectionConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
) -> PilotAnalysisResponse {
    if let Some(scanned_character_ids) = &request.scanned_character_ids {
        return match &request.group_inputs {
            Some(group_inputs) => build_group_response(
                request.character_id,
                scanned_character_ids,
                group_inputs,
                recent_window_configuration,
                group_detection_configuration,
            ),
            None => failed_pilot_response(request.character_id, "unreadable_request"),
        };
    }
    match request.pilot {
        Some(pilot) if pilot.character_id == request.character_id => build_pilot_response(
            request.character_id,
            PilotReadInputs::from(pilot),
            recent_window_configuration,
            style_configuration,
            threat_configuration,
            Utc::now(),
        ),
        _ => failed_pilot_response(request.character_id, "unreadable_request"),
    }
}

struct PilotReadInputs {
    all_killmails: Vec<RecentKillmailSnapshot>,
    statistics: Option<ZKillStatisticsSnapshot>,
    identity: Option<PilotIdentitySnapshot>,
    coverage_start_text: Option<String>,
}

impl From<PilotInputs> for PilotReadInputs {
    fn from(pilot: PilotInputs) -> Self {
        Self {
            all_killmails: pilot.killmails,
            statistics: pilot.statistics,
            identity: pilot.identity,
            coverage_start_text: pilot.coverage_start_utc,
        }
    }
}

fn build_pilot_response(
    character_id: i64,
    inputs: PilotReadInputs,
    recent_window_configuration: &RecentWindowConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
    now: DateTime<Utc>,
) -> PilotAnalysisResponse {
    let coverage_start_utc = inputs.coverage_start_text.and_then(|text| {
        DateTime::parse_from_rfc3339(&text)
            .ok()
            .map(|value| value.with_timezone(&Utc))
    });
    let recent_window_killmails = {
        let _timing = timing_recorder::scope("recent_window_filter");
        filter_recent_window(
            &inputs.all_killmails,
            recent_window_configuration.recent_window_days,
            now,
        )
    };
    if timing_recorder::is_active() {
        timing_recorder::add_count("killmail_rows", inputs.all_killmails.len() as i64);
        timing_recorder::add_count("recent_window_killmail_rows", recent_window_killmails.len() as i64);
    }
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
    let recent_style_result = {
        let _timing = timing_recorder::scope("recent_style");
        analyze_recent_style(
            RecentStyleRequest {
                character_id,
                killmails,
            },
            style_configuration,
        )
    };
    let threat = {
        let _timing = timing_recorder::scope("threat");
        analyze_intrinsic_threat(
            threat_configuration,
            inputs.statistics.as_ref(),
            &recent_window_killmails,
            inputs.identity.as_ref(),
            coverage_start_utc,
            now,
            recent_window_configuration.recent_window_days,
            &recent_style_result.recent_style,
        )
    };
    let derived_activity = {
        let _timing = timing_recorder::scope("derived_activity");
        DerivedActivityResponse::from(derive_activity(&inputs.all_killmails, now))
    };
    PilotAnalysisResponse {
        character_id,
        recent_style: Some(recent_style_result.recent_style),
        is_recent_podder: Some(recent_style_result.is_podder),
        threat: Some(threat),
        group_detection: None,
        derived_activity: Some(derived_activity),
        failure: None,
        timings_ms: None,
        timing_counts: None,
    }
}

fn failed_pilot_response(character_id: i64, reason: &str) -> PilotAnalysisResponse {
    PilotAnalysisResponse {
        character_id,
        recent_style: None,
        is_recent_podder: None,
        threat: None,
        group_detection: None,
        derived_activity: None,
        failure: Some(reason.to_string()),
        timings_ms: None,
        timing_counts: None,
    }
}

fn read_pilot_inputs(character_ids: &[i64], pilots: &[PilotInputs]) -> Vec<Result<PilotReadInputs, String>> {
    let mut by_character: HashMap<i64, &PilotInputs> = HashMap::new();
    for pilot in pilots {
        by_character.entry(pilot.character_id).or_insert(pilot);
    }
    character_ids
        .iter()
        .map(|character_id| match by_character.get(character_id) {
            Some(pilot) => Ok(PilotReadInputs::from((*pilot).clone())),
            None => Err("unreadable_request".to_string()),
        })
        .collect()
}

fn assemble_pilot_responses(
    character_ids: &[i64],
    inputs: Vec<Result<PilotReadInputs, String>>,
    recent_window_configuration: &RecentWindowConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
    now: DateTime<Utc>,
) -> Vec<PilotAnalysisResponse> {
    let _timing = timing_recorder::scope("pilot_loop_total");
    let mut analyzed = 0_i64;
    let mut failures = 0_i64;
    let results = character_ids
        .iter()
        .zip(inputs)
        .map(|(character_id, input)| match input {
            Ok(value) => {
                analyzed += 1;
                build_pilot_response(
                    *character_id,
                    value,
                    recent_window_configuration,
                    style_configuration,
                    threat_configuration,
                    now,
                )
            }
            Err(reason) => {
                failures += 1;
                failed_pilot_response(*character_id, &reason)
            }
        })
        .collect::<Vec<_>>();
    if timing_recorder::is_active() {
        timing_recorder::add_count("pilots_analyzed", analyzed);
        timing_recorder::add_count("pilot_failures", failures);
    }
    results
}

fn analyze_pilots_batch(
    character_ids: &[i64],
    pilots: &[PilotInputs],
    recent_window_configuration: &RecentWindowConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
) -> Vec<PilotAnalysisResponse> {
    let inputs = read_pilot_inputs(character_ids, pilots);
    assemble_pilot_responses(
        character_ids,
        inputs,
        recent_window_configuration,
        style_configuration,
        threat_configuration,
        Utc::now(),
    )
}

#[no_mangle]
pub extern "C" fn killright_analyze_pilots(request_json: *const c_char) -> *mut c_char {
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return pilots_failure_response("unreadable_request");
        }
        let request_text = unsafe { CStr::from_ptr(request_json) };
        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return pilots_failure_response("unreadable_request"),
        };
        let request = match serde_json::from_str::<PilotsAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return pilots_failure_response("unreadable_request"),
        };
        let timing_started = timing_recorder::begin();
        let Some((
            threat_configuration,
            recent_window_configuration,
            _group_detection_configuration,
            style_configuration,
        )) = get_runtime_state()
        else {
            return pilots_failure_response("missing_runtime");
        };
        let results = analyze_pilots_batch(
            &request.character_ids,
            &request.pilots,
            &recent_window_configuration,
            &style_configuration,
            &threat_configuration,
        );
        timed_pilots_response_json(
            PilotsAnalysisResponse {
                results,
                failure: None,
                timings_ms: None,
                timing_counts: None,
            },
            timing_started,
            "batch_total",
        )
    });
    result.unwrap_or_else(|_| pilots_failure_response("internal_error"))
}

fn pilots_failure_response(reason: &str) -> *mut c_char {
    timing_recorder::clear();
    pilots_response_json(PilotsAnalysisResponse {
        results: Vec::new(),
        failure: Some(reason.to_string()),
        timings_ms: None,
        timing_counts: None,
    })
}

fn timed_pilots_response_json(
    mut response: PilotsAnalysisResponse,
    timing_started: Option<Instant>,
    total_phase: &str,
) -> *mut c_char {
    if let Some(started) = timing_started {
        let serialize_started = Instant::now();
        let _ = serde_json::to_string(&response);
        timing_recorder::record_ms("response_build", serialize_started.elapsed().as_secs_f64() * 1000.0);
        timing_recorder::record_ms(total_phase, started.elapsed().as_secs_f64() * 1000.0);
        if let Some(snapshot) = timing_recorder::take() {
            response.timings_ms = Some(snapshot.timings_ms);
            response.timing_counts = Some(snapshot.timing_counts);
        }
    }
    pilots_response_json(response)
}

fn pilots_response_json(response: PilotsAnalysisResponse) -> *mut c_char {
    let json = serde_json::to_string(&response).unwrap_or_else(|_| {
        "{\"results\":[],\"failure\":\"internal_error\"}".to_string()
    });
    CString::new(json).unwrap_or_else(|_| {
        CString::new("{\"results\":[],\"failure\":\"internal_error\"}").unwrap()
    }).into_raw()
}

fn build_group_response(
    character_id: i64,
    scanned_character_ids: &[i64],
    inputs: &GroupInputs,
    recent_window_configuration: &RecentWindowConfiguration,
    group_detection_configuration: &GroupDetectionConfiguration,
) -> PilotAnalysisResponse {
    let npc_corporation_ids = inputs.npc_corporation_ids.iter().copied().collect::<HashSet<i64>>();
    if timing_recorder::is_active() {
        timing_recorder::add_count("scanned_pilots", scanned_character_ids.len() as i64);
        timing_recorder::add_count("direct_evidence_rows", inputs.direct_evidence.len() as i64);
        timing_recorder::add_count("chain_evidence_rows", inputs.chain_evidence.len() as i64);
    }

    let direct_relationships = {
        let _timing = timing_recorder::scope("direct_analysis");
        analyze_direct_relationships(
            &inputs.direct_evidence,
            &inputs.identities,
            &npc_corporation_ids,
            group_detection_configuration.minimum_shared_events,
        )
    };
    let chained_relationships = {
        let _timing = timing_recorder::scope("chain_analysis");
        analyze_chained_relationships(
            &inputs.chain_evidence,
            &inputs.identities,
            &npc_corporation_ids,
            group_detection_configuration.minimum_shared_events,
            recent_window_configuration.recent_window_days,
            Utc::now(),
        )
    };

    let scored_relationships = {
        let _timing = timing_recorder::scope("scoring");
        score_and_select_relationships(
            &direct_relationships,
            &chained_relationships,
            group_detection_configuration,
            recent_window_configuration.recent_window_days,
        )
    };
    if timing_recorder::is_active() {
        timing_recorder::add_count("direct_relationships", direct_relationships.len() as i64);
        timing_recorder::add_count("chain_relationships", chained_relationships.len() as i64);
        timing_recorder::add_count("scored_relationships", scored_relationships.len() as i64);
    }

    let relationships: Vec<GroupRelationshipResponse> = {
        let _timing = timing_recorder::scope("response_build");
        scored_relationships.into_iter().map(GroupRelationshipResponse::from).collect()
    };

    PilotAnalysisResponse {
        character_id,
        recent_style: None,
        is_recent_podder: None,
        threat: None,
        group_detection: Some(GroupDetectionResponse { relationships }),
        derived_activity: None,
        failure: None,
        timings_ms: None,
        timing_counts: None,
    }
}

fn diagnose_group_detection(
    request: &PilotAnalysisRequest,
    recent_window_configuration: &RecentWindowConfiguration,
    group_detection_configuration: &GroupDetectionConfiguration,
) -> GroupDetectionDiagnosticsEnvelope {
    let failure = |reason: &str| GroupDetectionDiagnosticsEnvelope {
        character_id: request.character_id,
        diagnostics: None,
        failure: Some(reason.to_string()),
    };
    if request.scanned_character_ids.is_none() {
        return failure("missing_scanned_character_ids");
    }
    let Some(inputs) = &request.group_inputs else {
        return failure("unreadable_request");
    };
    let npc_corporation_ids = inputs.npc_corporation_ids.iter().copied().collect::<HashSet<i64>>();
    let diagnostics = run_group_detection_diagnostics(
        &inputs.direct_evidence,
        &inputs.chain_evidence,
        &inputs.identities,
        group_detection_configuration,
        &npc_corporation_ids,
        recent_window_configuration.recent_window_days,
        Utc::now(),
    );

    GroupDetectionDiagnosticsEnvelope {
        character_id: request.character_id,
        diagnostics: Some(diagnostics.into()),
        failure: None,
    }
}

fn diagnose_threat(
    request: &PilotAnalysisRequest,
    recent_window_configuration: &RecentWindowConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
) -> ThreatDiagnosticsEnvelope {
    let Some(pilot) = request.pilot.as_ref().filter(|pilot| pilot.character_id == request.character_id) else {
        return ThreatDiagnosticsEnvelope {
            character_id: request.character_id,
            diagnostics: None,
            failure: Some("unreadable_request".to_string()),
        };
    };
    let coverage_start_utc = pilot.coverage_start_utc.as_ref().and_then(|text| {
        DateTime::parse_from_rfc3339(text)
            .ok()
            .map(|value| value.with_timezone(&Utc))
    });

    let now = Utc::now();
    let recent_window_killmails = filter_recent_window(
        &pilot.killmails,
        recent_window_configuration.recent_window_days,
        now,
    );

    let recent_style_result = analyze_recent_style(
        RecentStyleRequest {
            character_id: request.character_id,
            killmails: recent_window_killmails
                .iter()
                .map(|row| RecentKillmailInput {
                    killmail_id: row.killmail_id,
                    is_loss: row.is_loss,
                    attacker_count: row.attacker_count,
                    is_solo: row.is_solo,
                    ship_type_id: row.ship_type_id,
                })
                .collect(),
        },
        style_configuration,
    );

    let diagnostics = analyze_intrinsic_threat_diagnostics(
        threat_configuration,
        pilot.statistics.as_ref(),
        &recent_window_killmails,
        pilot.identity.as_ref(),
        coverage_start_utc,
        now,
        recent_window_configuration.recent_window_days,
        &recent_style_result.recent_style,
    );

    ThreatDiagnosticsEnvelope {
        character_id: request.character_id,
        diagnostics: Some(diagnostics.into()),
        failure: None,
    }
}

#[no_mangle]
pub extern "C" fn killright_diagnose_group_detection(request_json: *const c_char) -> *mut c_char {
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
        let Some((
            _threat_configuration,
            recent_window_configuration,
            group_detection_configuration,
            _style_configuration,
        )) = get_runtime_state()
        else {
            return group_detection_diagnostics_failure(request.character_id, "missing_runtime");
        };

        group_detection_diagnostics_json(diagnose_group_detection(
            &request,
            &recent_window_configuration,
            &group_detection_configuration,
        ))
    });
    result.unwrap_or_else(|_| group_detection_diagnostics_failure(0, "internal_error"))
}

#[no_mangle]
pub extern "C" fn killright_diagnose_threat(request_json: *const c_char) -> *mut c_char {
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
        let Some((
            threat_configuration,
            recent_window_configuration,
            _group_detection_configuration,
            style_configuration,
        )) = get_runtime_state()
        else {
            return threat_diagnostics_failure(request.character_id, "missing_runtime");
        };

        threat_diagnostics_json(diagnose_threat(
            &request,
            &recent_window_configuration,
            &style_configuration,
            &threat_configuration,
        ))
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
pub extern "C" fn killright_shutdown() {
    let _ = panic::catch_unwind(|| {
        timing_recorder::set_enabled(false);
        if let Some(cell) = RUNTIME.get() {
            if let Ok(mut guard) = cell.lock() {
                *guard = None;
            }
        }
    });
}
#[no_mangle]
pub extern "C" fn killright_free_string(value: *mut c_char) {
    if value.is_null() {
        return;
    }
    unsafe {
        let _ = CString::from_raw(value);
    }
}
fn get_runtime_state() -> Option<(
    ThreatConfiguration,
    RecentWindowConfiguration,
    GroupDetectionConfiguration,
    StyleConfiguration,
)> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some((
        runtime.threat_configuration.clone(),
        runtime.recent_window_configuration.clone(),
        runtime.group_detection_configuration.clone(),
        runtime.style_configuration.clone(),
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
    timing_recorder::clear();
    response_json(PilotAnalysisResponse {
        character_id,
        recent_style: None,
        is_recent_podder: None,
        threat: None,
        group_detection: None,
        derived_activity: None,
        failure: Some(reason.to_string()),
        timings_ms: None,
        timing_counts: None,
    })
}
fn timed_response_json(
    mut response: PilotAnalysisResponse,
    timing_started: Option<Instant>,
    total_phase: &str,
) -> *mut c_char {
    if let Some(started) = timing_started {
        let serialize_started = Instant::now();
        let _ = serde_json::to_string(&response);
        timing_recorder::record_ms("response_build", serialize_started.elapsed().as_secs_f64() * 1000.0);
        timing_recorder::record_ms(total_phase, started.elapsed().as_secs_f64() * 1000.0);
        if let Some(snapshot) = timing_recorder::take() {
            response.timings_ms = Some(snapshot.timings_ms);
            response.timing_counts = Some(snapshot.timing_counts);
        }
    }
    response_json(response)
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
    use kr_engine::contracts::KillmailAttackerEvidence;

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

    const LUKAS: i64 = 95465499;
    const TRAL: i64 = 91321792;
    const SYMPTOM: i64 = 2112625428;

    fn ago(days: i64) -> String {
        (Utc::now() - Duration::days(days)).to_rfc3339()
    }

    fn fixture_killmail(
        killmail_id: i64,
        character_id: i64,
        kill_time_utc: &str,
        is_loss: bool,
        attacker_count: i32,
        ship_type_id: i64,
    ) -> RecentKillmailSnapshot {
        RecentKillmailSnapshot {
            killmail_id,
            killmail_hash: Some(format!("h{killmail_id}")),
            character_id,
            kill_time_utc: kill_time_utc.to_string(),
            is_loss,
            attacker_count,
            is_solo: attacker_count == 1,
            ship_type_id: Some(ship_type_id),
            system_id: Some(30000142),
            location_id: None,
            is_npc: false,
            cached_at_utc: kill_time_utc.to_string(),
        }
    }

    fn fixture_pilots() -> Vec<PilotInputs> {
        let recent = ago(1);
        let older = ago(3);
        let old = ago(10);
        vec![
            PilotInputs {
                character_id: LUKAS,
                killmails: vec![
                    fixture_killmail(1, LUKAS, &recent, false, 1, 587),
                    fixture_killmail(2, LUKAS, &older, false, 2, 670),
                    fixture_killmail(4, LUKAS, &older, false, 2, 587),
                ],
                statistics: Some(ZKillStatisticsSnapshot {
                    character_id: LUKAS,
                    ships_destroyed: 120,
                    solo_kills: 30,
                    solo_ratio: 0.25,
                    avg_gang_size: 4.0,
                    ships_lost: 12,
                    solo_losses: 2,
                    general_style: "Gang".to_string(),
                    checked_at_utc: recent.clone(),
                    no_history_marker: false,
                    pod_losses: 0,
                }),
                identity: Some(fixture_identity(LUKAS, "Lukas Naarii", 98000001, None)),
                coverage_start_utc: Some(old.clone()),
            },
            PilotInputs {
                character_id: TRAL,
                killmails: vec![fixture_killmail(3, TRAL, &old, true, 3, 587)],
                statistics: None,
                identity: None,
                coverage_start_utc: None,
            },
            PilotInputs {
                character_id: SYMPTOM,
                killmails: vec![fixture_killmail(4, SYMPTOM, &older, false, 2, 587)],
                statistics: None,
                identity: None,
                coverage_start_utc: None,
            },
        ]
    }

    fn fixture_identity(
        character_id: i64,
        name: &str,
        corporation_id: i64,
        alliance_id: Option<i64>,
    ) -> PilotIdentitySnapshot {
        PilotIdentitySnapshot {
            input_name: name.to_uppercase(),
            character_id: Some(character_id),
            character_name: Some(name.to_string()),
            verify_status: "Partial".to_string(),
            security_status: Some(1.5),
            corporation_id: Some(corporation_id),
            corporation_name: None,
            corporation_ticker: None,
            alliance_id,
            alliance_name: None,
            alliance_ticker: None,
            cached_at_utc: "2026-09-20T00:00:00+00:00".to_string(),
        }
    }

    fn configurations() -> (RecentWindowConfiguration, StyleConfiguration, ThreatConfiguration) {
        let threat = serde_json::from_str(
            crate::kr_engine::threat_analysis::threat_test_fixtures::THREAT_CONFIGURATION_JSON,
        )
        .unwrap();
        let style = StyleConfiguration {
            blob_minimum_average_attackers: 5.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
        };

        (RecentWindowConfiguration { recent_window_days: 14 }, style, threat)
    }

    fn group_configuration() -> GroupDetectionConfiguration {
        serde_json::from_str(
            r#"{
                "minimumSharedEvents": 2,
                "strengthStep": 10,
                "gangSizeWeights": [
                    { "maximumGangSize": 3, "weight": 1.0 },
                    { "maximumGangSize": 5, "weight": 0.75 },
                    { "maximumGangSize": 7, "weight": 0.5 },
                    { "maximumGangSize": 10, "weight": 0.25 }
                ],
                "sampleFactor": { "minimum": 0.5, "maximum": 1.0, "saturatesAtCountedSharedKills": 10 },
                "splitBonus": 20,
                "chainDiscount": 0.75,
                "intermediaryBonus": { "perAdditional": 10, "maximum": 30 }
            }"#,
        )
        .unwrap()
    }

    fn evidence(
        killmail_id: i64,
        character_id: i64,
        corporation_id: i64,
        kill_time_utc: &str,
    ) -> KillmailAttackerEvidence {
        KillmailAttackerEvidence {
            killmail_id,
            character_id,
            corporation_id: Some(corporation_id),
            alliance_id: None,
            kill_time_utc: kill_time_utc.to_string(),
            unique_attacker_count: 2,
        }
    }

    fn fixture_group_inputs() -> GroupInputs {
        let first = ago(2);
        let second = ago(1);
        GroupInputs {
            direct_evidence: vec![
                evidence(901, LUKAS, 98000001, &first),
                evidence(901, TRAL, 98000002, &first),
                evidence(902, LUKAS, 98000001, &second),
                evidence(902, TRAL, 98000002, &second),
            ],
            chain_evidence: Vec::new(),
            identities: vec![
                fixture_identity(LUKAS, "Lukas Naarii", 98000001, None),
                fixture_identity(TRAL, "T'ral Vsengne", 98000002, None),
            ],
            npc_corporation_ids: vec![1000001],
        }
    }

    fn response_text(pointer: *mut c_char) -> String {
        let text = unsafe { CStr::from_ptr(pointer) }.to_str().unwrap().to_string();
        killright_free_string(pointer);
        text
    }

    #[test]
    fn batch_returns_one_result_per_pilot_in_request_order() {
        let (window, style, threat) = configurations();

        let results = analyze_pilots_batch(&[SYMPTOM, LUKAS, TRAL], &fixture_pilots(), &window, &style, &threat);

        assert_eq!(
            results.iter().map(|result| result.character_id).collect::<Vec<_>>(),
            vec![SYMPTOM, LUKAS, TRAL]
        );
        assert!(results.iter().all(|result| result.failure.is_none()));
        assert!(results.iter().all(|result| result.derived_activity.is_some()));
    }

    #[test]
    fn batch_result_equals_the_single_pilot_assembly_for_each_pilot() {
        let (window, style, threat) = configurations();
        let pilots = fixture_pilots();
        let ids = [LUKAS, TRAL, SYMPTOM];
        let now = Utc::now();

        let batch = assemble_pilot_responses(&ids, read_pilot_inputs(&ids, &pilots), &window, &style, &threat, now);
        let singles = ids
            .iter()
            .map(|id| {
                assemble_pilot_responses(&[*id], read_pilot_inputs(&[*id], &pilots), &window, &style, &threat, now)
                    .remove(0)
            })
            .collect::<Vec<_>>();

        for (from_batch, single) in batch.iter().zip(singles.iter()) {
            assert_eq!(
                serde_json::to_string(from_batch).unwrap(),
                serde_json::to_string(single).unwrap()
            );
        }
    }

    #[test]
    fn derived_views_carry_weekly_counts_and_newest_killmail() {
        let (window, style, threat) = configurations();

        let results = analyze_pilots_batch(&[LUKAS, TRAL, SYMPTOM], &fixture_pilots(), &window, &style, &threat);

        let lukas = results[0].derived_activity.as_ref().unwrap();
        assert!(lukas.has_public_activity_data);
        assert_eq!(lukas.kills_week, Some(2));
        assert_eq!(lukas.solo_week, Some(1));
        assert_eq!(lukas.newest_non_pod_killmail.as_ref().unwrap().activity_type, "Kill");

        let tral = results[1].derived_activity.as_ref().unwrap();
        assert!(!tral.has_public_activity_data);
        assert_eq!(tral.kills_week, None);
        assert!(tral.newest_non_pod_killmail.is_none());

        let symptom = results[2].derived_activity.as_ref().unwrap();
        assert!(symptom.has_public_activity_data);
        assert_eq!(symptom.kills_week, Some(1));
    }

    #[test]
    fn one_pilots_read_error_yields_a_per_pilot_failure_and_the_rest_succeed() {
        let (window, style, threat) = configurations();
        let inputs = vec![
            Ok(PilotReadInputs {
                all_killmails: Vec::new(),
                statistics: None,
                identity: None,
                coverage_start_text: None,
            }),
            Err("repository_read_error:killmails".to_string()),
            Ok(PilotReadInputs {
                all_killmails: Vec::new(),
                statistics: None,
                identity: None,
                coverage_start_text: None,
            }),
        ];

        let results = assemble_pilot_responses(&[LUKAS, TRAL, SYMPTOM], inputs, &window, &style, &threat, Utc::now());

        assert!(results[0].failure.is_none());
        assert_eq!(results[1].failure.as_deref(), Some("repository_read_error:killmails"));
        assert!(results[1].recent_style.is_none());
        assert!(results[1].derived_activity.is_none());
        assert!(results[2].failure.is_none());
    }

    #[test]
    fn pilot_without_an_entry_in_the_request_fails_with_unreadable_request_and_the_rest_succeed() {
        let (window, style, threat) = configurations();
        let pilots = fixture_pilots()
            .into_iter()
            .filter(|pilot| pilot.character_id != TRAL)
            .collect::<Vec<_>>();

        let results = analyze_pilots_batch(&[LUKAS, TRAL, SYMPTOM], &pilots, &window, &style, &threat);

        assert!(results[0].failure.is_none());
        assert_eq!(results[1].failure.as_deref(), Some("unreadable_request"));
        assert!(results[1].recent_style.is_none());
        assert!(results[2].failure.is_none());
    }

    #[test]
    fn recorder_aggregates_across_the_batch() {
        let (window, style, threat) = configurations();
        timing_recorder::begin_forced();

        analyze_pilots_batch(&[LUKAS, TRAL, SYMPTOM], &fixture_pilots(), &window, &style, &threat);
        let snapshot = timing_recorder::take().expect("recorder was active");

        assert_eq!(snapshot.timing_counts["pilots_analyzed"], 3);
        assert_eq!(snapshot.timing_counts["pilot_failures"], 0);
        assert!(snapshot.timings_ms.contains_key("pilot_loop_total"));
        assert!(snapshot.timings_ms.contains_key("derived_activity"));
        assert!(!snapshot.timings_ms.contains_key("open_connection"));
        assert!(!snapshot.timings_ms.contains_key("killmails_query"));
    }

    #[test]
    fn batch_export_reports_unreadable_request_for_null_and_malformed_input() {
        assert_eq!(
            response_text(killright_analyze_pilots(std::ptr::null())),
            "{\"results\":[],\"failure\":\"unreadable_request\"}"
        );

        let malformed = CString::new("{\"character_ids\":\"nope\"}").unwrap();

        assert_eq!(
            response_text(killright_analyze_pilots(malformed.as_ptr())),
            "{\"results\":[],\"failure\":\"unreadable_request\"}"
        );
    }

    #[test]
    fn batch_request_deserialises_with_and_without_inputs() {
        let without = serde_json::from_str::<PilotsAnalysisRequest>("{\"character_ids\":[95465499]}").unwrap();
        assert_eq!(without.character_ids, vec![LUKAS]);
        assert!(without.pilots.is_empty());

        let with = serde_json::from_str::<PilotsAnalysisRequest>(
            "{\"character_ids\":[95465499],\"pilots\":[{\"character_id\":95465499,\"killmails\":[{\"killmail_id\":1,\"killmail_hash\":null,\"character_id\":95465499,\"kill_time_utc\":\"2026-09-20T00:00:00+00:00\",\"is_loss\":false,\"attacker_count\":2,\"is_solo\":false,\"ship_type_id\":587,\"system_id\":30000142,\"location_id\":null,\"is_npc\":false,\"cached_at_utc\":\"2026-09-20T00:00:00+00:00\"}],\"statistics\":null,\"identity\":null,\"coverage_start_utc\":\"2026-09-08T00:00:00+00:00\"}]}",
        )
        .unwrap();
        assert_eq!(with.pilots.len(), 1);
        assert_eq!(with.pilots[0].killmails[0].ship_type_id, Some(587));
        assert!(with.pilots[0].statistics.is_none());
        assert_eq!(with.pilots[0].coverage_start_utc.as_deref(), Some("2026-09-08T00:00:00+00:00"));
    }

    #[test]
    fn pilot_request_deserialises_with_and_without_inputs() {
        let without = serde_json::from_str::<PilotAnalysisRequest>("{\"character_id\":95465499}").unwrap();
        assert!(without.scanned_character_ids.is_none());
        assert!(without.pilot.is_none());
        assert!(without.group_inputs.is_none());

        let group = serde_json::from_str::<PilotAnalysisRequest>(
            "{\"character_id\":95465499,\"scanned_character_ids\":[95465499,91321792],\"group_inputs\":{\"direct_evidence\":[{\"killmail_id\":1,\"character_id\":95465499,\"corporation_id\":98000001,\"alliance_id\":null,\"kill_time_utc\":\"2026-09-20T00:00:00+00:00\",\"unique_attacker_count\":2}],\"chain_evidence\":[],\"identities\":[],\"npc_corporation_ids\":[1000001]}}",
        )
        .unwrap();
        assert_eq!(group.group_inputs.as_ref().unwrap().direct_evidence.len(), 1);
        assert_eq!(group.group_inputs.as_ref().unwrap().npc_corporation_ids, vec![1000001]);
    }

    #[test]
    fn single_pilot_request_over_inputs_matches_the_batch_result() {
        let (window, style, threat) = configurations();
        let pilots = fixture_pilots();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: None,
            pilot: Some(pilots[0].clone()),
            group_inputs: None,
        };

        let single = respond_to_pilot_request(request, &window, &group_configuration(), &style, &threat);
        let batch = analyze_pilots_batch(&[LUKAS], &pilots, &window, &style, &threat).remove(0);

        assert!(single.failure.is_none());
        assert_eq!(single.recent_style, batch.recent_style);
        assert_eq!(single.threat, batch.threat);
        assert_eq!(single.derived_activity, batch.derived_activity);
    }

    #[test]
    fn single_pilot_request_without_inputs_or_with_another_pilots_inputs_is_unreadable() {
        let (window, style, threat) = configurations();
        let pilots = fixture_pilots();

        let missing = respond_to_pilot_request(
            PilotAnalysisRequest { character_id: LUKAS, scanned_character_ids: None, pilot: None, group_inputs: None },
            &window,
            &group_configuration(),
            &style,
            &threat,
        );
        let mismatched = respond_to_pilot_request(
            PilotAnalysisRequest {
                character_id: LUKAS,
                scanned_character_ids: None,
                pilot: Some(pilots[1].clone()),
                group_inputs: None,
            },
            &window,
            &group_configuration(),
            &style,
            &threat,
        );

        assert_eq!(missing.failure.as_deref(), Some("unreadable_request"));
        assert_eq!(mismatched.failure.as_deref(), Some("unreadable_request"));
    }

    #[test]
    fn group_detection_over_request_inputs_reports_the_direct_relationship() {
        let (window, style, threat) = configurations();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: Some(vec![LUKAS, TRAL]),
            pilot: None,
            group_inputs: Some(fixture_group_inputs()),
        };

        let response = respond_to_pilot_request(request, &window, &group_configuration(), &style, &threat);

        assert!(response.failure.is_none());
        let relationships = response.group_detection.unwrap().relationships;
        assert_eq!(relationships.len(), 1);
        assert_eq!(relationships[0].link_type, "Direct");
        assert_eq!(relationships[0].strength, 10);
        assert_eq!(relationships[0].confidence, 50);
        assert_eq!(relationships[0].total_shared_kills, Some(2));
    }

    #[test]
    fn group_detection_without_group_inputs_is_unreadable() {
        let (window, style, threat) = configurations();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: Some(vec![LUKAS, TRAL]),
            pilot: None,
            group_inputs: None,
        };

        let response = respond_to_pilot_request(request, &window, &group_configuration(), &style, &threat);

        assert_eq!(response.failure.as_deref(), Some("unreadable_request"));
        assert!(response.group_detection.is_none());
    }

    #[test]
    fn group_detection_diagnostics_over_request_inputs_explain_the_pair() {
        let (window, _style, _threat) = configurations();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: Some(vec![LUKAS, TRAL]),
            pilot: None,
            group_inputs: Some(fixture_group_inputs()),
        };

        let envelope = diagnose_group_detection(&request, &window, &group_configuration());

        assert!(envelope.failure.is_none());
        let diagnostics = envelope.diagnostics.unwrap();
        assert_eq!(diagnostics.direct_relationships.len(), 1);
        assert!(diagnostics.direct_relationships[0].qualifies);
        assert_eq!(diagnostics.direct_relationships[0].counted_shared_kills, 2);
        assert_eq!(diagnostics.direct_relationships[0].strength, Some(10));
    }

    #[test]
    fn group_detection_diagnostics_report_missing_scan_set_and_missing_inputs() {
        let (window, _style, _threat) = configurations();
        let without_scan_set = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: None,
            pilot: None,
            group_inputs: Some(fixture_group_inputs()),
        };
        let without_inputs = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: Some(vec![LUKAS]),
            pilot: None,
            group_inputs: None,
        };

        assert_eq!(
            diagnose_group_detection(&without_scan_set, &window, &group_configuration()).failure.as_deref(),
            Some("missing_scanned_character_ids")
        );
        assert_eq!(
            diagnose_group_detection(&without_inputs, &window, &group_configuration()).failure.as_deref(),
            Some("unreadable_request")
        );
    }

    #[test]
    fn threat_diagnostics_over_request_inputs_match_the_batch_threat_score() {
        let (window, style, threat) = configurations();
        let pilots = fixture_pilots();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: None,
            pilot: Some(pilots[0].clone()),
            group_inputs: None,
        };

        let envelope = diagnose_threat(&request, &window, &style, &threat);
        let batch = analyze_pilots_batch(&[LUKAS], &pilots, &window, &style, &threat).remove(0);

        assert!(envelope.failure.is_none());
        assert_eq!(envelope.diagnostics.unwrap().score, batch.threat.unwrap().score);
    }

    #[test]
    fn threat_diagnostics_without_inputs_are_unreadable() {
        let (window, style, threat) = configurations();
        let request = PilotAnalysisRequest {
            character_id: LUKAS,
            scanned_character_ids: None,
            pilot: None,
            group_inputs: None,
        };

        let envelope = diagnose_threat(&request, &window, &style, &threat);

        assert_eq!(envelope.failure.as_deref(), Some("unreadable_request"));
        assert!(envelope.diagnostics.is_none());
    }
}
