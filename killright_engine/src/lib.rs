use chrono::{DateTime, Duration, Utc};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};
use std::time::Instant;
#[path = "kr_engine.rs"]
pub mod kr_engine;
use kr_engine::activity_analysis::derive_activity;
use kr_engine::contracts::{
    DerivedActivityResponse, GroupDetectionDiagnosticsEnvelope, GroupDetectionResponse, GroupRelationshipResponse,
    PilotAnalysisRequest, PilotAnalysisResponse, PilotsAnalysisRequest, PilotsAnalysisResponse,
    ThreatDiagnosticsEnvelope,
};
use kr_engine::group_analysis::chained_relationship_analyzer::analyze_chained_relationships;
use kr_engine::group_analysis::direct_relationship_analyzer::analyze_direct_relationships;
use kr_engine::group_analysis::group_detection_configuration::GroupDetectionConfiguration;
use kr_engine::group_analysis::group_detection_diagnostics::run_group_detection_diagnostics;
use kr_engine::group_analysis::group_relationship_scoring::score_and_select_relationships;
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::repositories::activity_cache_repository::ActivityCacheRepository;
use kr_engine::repositories::duckdb_database::open_connection;
use kr_engine::repositories::killmail_relationship_repository::KillmailRelationshipRepository;
use kr_engine::repositories::pilot_identity_repository::{PilotIdentityRepository, PilotIdentitySnapshot};
use kr_engine::repositories::recent_killmail_repository::{RecentKillmailRepository, RecentKillmailSnapshot};
use kr_engine::repositories::sde_npc_corporation_repository::SdeNpcCorporationRepository;
use kr_engine::repositories::zkill_statistics_repository::{ZKillStatisticsRepository, ZKillStatisticsSnapshot};
use kr_engine::shared::database_path::get_database_path;
use kr_engine::shared::recent_window_configuration::RecentWindowConfiguration;
use kr_engine::shared::settings_loader::load_engine_settings;
use kr_engine::shared::style_configuration::StyleConfiguration;
use kr_engine::shared::timing_recorder;
use kr_engine::threat_analysis::{analyze_intrinsic_threat, analyze_intrinsic_threat_diagnostics, ThreatConfiguration};
pub use kr_engine::*;
struct RuntimeState {
    database_path: PathBuf,
    threat_configuration: ThreatConfiguration,
    recent_window_configuration: RecentWindowConfiguration,
    group_detection_configuration: GroupDetectionConfiguration,
    style_configuration: StyleConfiguration,
}
static RUNTIME: OnceLock<Mutex<Option<RuntimeState>>> = OnceLock::new();
#[no_mangle]
pub extern "C" fn killright_initialize() -> i32 {
    let result = panic::catch_unwind(|| {
        let database_path = match get_database_path() {
            Ok(value) => value,
            Err(_) => return 0,
        };
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
            database_path,
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
            database_path,
            threat_configuration,
            recent_window_configuration,
            group_detection_configuration,
            style_configuration,
        )) = get_runtime_state()
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
                timing_started,
            );
        }

        let recent_killmail_repository = RecentKillmailRepository::new(database_path.clone());
        let zkill_statistics_repository = ZKillStatisticsRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path.clone());
        let activity_cache_repository = ActivityCacheRepository::new(database_path);

        let all_killmails = {
            let _timing = timing_recorder::scope("killmails_query");
            match recent_killmail_repository.get_for_character(request.character_id) {
                Ok(value) => value,
                Err(_) => return failure_response(request.character_id, "repository_read_error:killmails"),
            }
        };
        let statistics = {
            let _timing = timing_recorder::scope("statistics_query");
            match zkill_statistics_repository.get_for_character(request.character_id) {
                Ok(value) => value,
                Err(_) => return failure_response(request.character_id, "repository_read_error:statistics"),
            }
        };
        let identity = {
            let _timing = timing_recorder::scope("identity_query");
            match pilot_identity_repository.get_for_character(request.character_id) {
                Ok(value) => value,
                Err(_) => return failure_response(request.character_id, "repository_read_error:identity"),
            }
        };
        let coverage_start_text = {
            let _timing = timing_recorder::scope("activity_cache_query");
            match activity_cache_repository.get_coverage_start_for_character(request.character_id) {
                Ok(value) => value,
                Err(_) => return failure_response(request.character_id, "repository_read_error:activity_cache"),
            }
        };
        timed_response_json(
            build_pilot_response(
                request.character_id,
                PilotReadInputs {
                    all_killmails,
                    statistics,
                    identity,
                    coverage_start_text,
                },
                &recent_window_configuration,
                &style_configuration,
                &threat_configuration,
                Utc::now(),
            ),
            timing_started,
            "pilot_total",
        )
    });
    result.unwrap_or_else(|_| failure_response(0, "internal_error"))
}

struct PilotReadInputs {
    all_killmails: Vec<RecentKillmailSnapshot>,
    statistics: Option<ZKillStatisticsSnapshot>,
    identity: Option<PilotIdentitySnapshot>,
    coverage_start_text: Option<String>,
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

fn read_pilot_inputs(database_path: &Path, character_ids: &[i64]) -> Vec<Result<PilotReadInputs, String>> {
    let fail_all = |reason: &str| {
        character_ids
            .iter()
            .map(|_| Err(reason.to_string()))
            .collect::<Vec<Result<PilotReadInputs, String>>>()
    };
    let connection = match open_connection(database_path) {
        Ok(value) => value,
        Err(_) => return fail_all("repository_read_error:killmails"),
    };
    let all_killmails = {
        let _timing = timing_recorder::scope("killmails_query");
        match RecentKillmailRepository::get_for_characters_on(&connection, character_ids) {
            Ok(value) => value,
            Err(_) => return fail_all("repository_read_error:killmails"),
        }
    };
    let statistics = {
        let _timing = timing_recorder::scope("statistics_query");
        match ZKillStatisticsRepository::get_for_characters_on(&connection, character_ids) {
            Ok(value) => value,
            Err(_) => return fail_all("repository_read_error:statistics"),
        }
    };
    let identities = {
        let _timing = timing_recorder::scope("identity_query");
        match PilotIdentityRepository::get_for_characters_on(&connection, character_ids) {
            Ok(value) => PilotIdentityRepository::first_by_character(value),
            Err(_) => return fail_all("repository_read_error:identity"),
        }
    };
    let coverage_starts = {
        let _timing = timing_recorder::scope("activity_cache_query");
        match ActivityCacheRepository::get_coverage_starts_on(&connection, character_ids) {
            Ok(value) => value,
            Err(_) => return fail_all("repository_read_error:activity_cache"),
        }
    };
    drop(connection);
    character_ids
        .iter()
        .map(|character_id| {
            Ok(PilotReadInputs {
                all_killmails: all_killmails.get(character_id).cloned().unwrap_or_default(),
                statistics: statistics.get(character_id).cloned(),
                identity: identities.get(character_id).cloned(),
                coverage_start_text: coverage_starts.get(character_id).cloned(),
            })
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
    database_path: &Path,
    recent_window_configuration: &RecentWindowConfiguration,
    style_configuration: &StyleConfiguration,
    threat_configuration: &ThreatConfiguration,
) -> Vec<PilotAnalysisResponse> {
    let inputs = read_pilot_inputs(database_path, character_ids);
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
            database_path,
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
            &database_path,
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

fn analyze_group_detection(
    character_id: i64,
    scanned_character_ids: &[i64],
    database_path: PathBuf,
    recent_window_configuration: &RecentWindowConfiguration,
    group_detection_configuration: &GroupDetectionConfiguration,
    timing_started: Option<Instant>,
) -> *mut c_char {
    let killmail_relationship_repository = KillmailRelationshipRepository::new(database_path.clone());
    let pilot_identity_repository = PilotIdentityRepository::new(database_path.clone());
    let sde_npc_corporation_repository = SdeNpcCorporationRepository::new(database_path);

    let direct_evidence = {
        let _timing = timing_recorder::scope("direct_evidence_query");
        match killmail_relationship_repository.get_attacker_evidence_for_scan_set(scanned_character_ids) {
            Ok(value) => value,
            Err(_) => return failure_response(character_id, "repository_read_error:direct_evidence"),
        }
    };
    let chain_evidence = {
        let _timing = timing_recorder::scope("chain_evidence_query");
        match killmail_relationship_repository.get_qualifying_attacker_evidence_touching_scan_set(scanned_character_ids) {
            Ok(value) => value,
            Err(_) => return failure_response(character_id, "repository_read_error:chain_evidence"),
        }
    };
    let current_identities = {
        let _timing = timing_recorder::scope("identities_query");
        match pilot_identity_repository.get_for_characters(scanned_character_ids) {
            Ok(value) => value,
            Err(_) => return failure_response(character_id, "repository_read_error:identity"),
        }
    };
    let npc_corporation_ids = {
        let _timing = timing_recorder::scope("npc_corporations_query");
        match sde_npc_corporation_repository.get_all_ids() {
            Ok(value) => value,
            Err(_) => return failure_response(character_id, "repository_read_error:npc_corporations"),
        }
    };
    if timing_recorder::is_active() {
        timing_recorder::add_count("scanned_pilots", scanned_character_ids.len() as i64);
        timing_recorder::add_count("direct_evidence_rows", direct_evidence.len() as i64);
        timing_recorder::add_count("chain_evidence_rows", chain_evidence.len() as i64);
    }

    let direct_relationships = {
        let _timing = timing_recorder::scope("direct_analysis");
        analyze_direct_relationships(
            &direct_evidence,
            &current_identities,
            &npc_corporation_ids,
            group_detection_configuration.minimum_shared_events,
        )
    };
    let chained_relationships = {
        let _timing = timing_recorder::scope("chain_analysis");
        analyze_chained_relationships(
            &chain_evidence,
            &current_identities,
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

    timed_response_json(
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
        },
        timing_started,
        "group_total",
    )
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
            database_path,
            _threat_configuration,
            recent_window_configuration,
            group_detection_configuration,
            _style_configuration,
        )) = get_runtime_state()
        else {
            return group_detection_diagnostics_failure(request.character_id, "missing_runtime");
        };
        let Some(scanned_character_ids) = &request.scanned_character_ids else {
            return group_detection_diagnostics_failure(request.character_id, "missing_scanned_character_ids");
        };

        let killmail_relationship_repository = KillmailRelationshipRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path.clone());
        let sde_npc_corporation_repository = SdeNpcCorporationRepository::new(database_path);

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
        let npc_corporation_ids = match sde_npc_corporation_repository.get_all_ids() {
            Ok(value) => value,
            Err(_) => return group_detection_diagnostics_failure(request.character_id, "repository_read_error:npc_corporations"),
        };

        let diagnostics = run_group_detection_diagnostics(
            &direct_evidence,
            &chain_evidence,
            &current_identities,
            &group_detection_configuration,
            &npc_corporation_ids,
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
            database_path,
            threat_configuration,
            recent_window_configuration,
            _group_detection_configuration,
            _style_configuration,
        )) = get_runtime_state()
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
    PathBuf,
    ThreatConfiguration,
    RecentWindowConfiguration,
    GroupDetectionConfiguration,
    StyleConfiguration,
)> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some((
        runtime.database_path.clone(),
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

    fn unique_suffix() -> u128 {
        static COUNTER: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();

        nanos * 1000 + u128::from(COUNTER.fetch_add(1, std::sync::atomic::Ordering::SeqCst) % 1000)
    }

    fn ago(days: i64) -> String {
        (Utc::now() - Duration::days(days)).to_rfc3339()
    }

    fn create_batch_database() -> PathBuf {
        let path = std::env::temp_dir().join(format!("analyze-pilots-{}.duckdb", unique_suffix()));
        let connection = duckdb::Connection::open(&path).unwrap();
        connection
            .execute_batch(
                "CREATE TABLE zkill_killmails (
                    killmail_id BIGINT PRIMARY KEY, killmail_hash TEXT, kill_time_utc TEXT NOT NULL,
                    system_id BIGINT NOT NULL, location_id BIGINT, victim_character_id BIGINT,
                    victim_ship_type_id BIGINT, unique_attacker_count INTEGER NOT NULL, is_solo BOOLEAN NOT NULL,
                    is_npc BOOLEAN NOT NULL, is_qualifying BOOLEAN NOT NULL, cached_at_utc TEXT NOT NULL
                );
                CREATE TABLE zkill_killmail_attackers (
                    killmail_id BIGINT NOT NULL, character_id BIGINT NOT NULL, corporation_id BIGINT,
                    alliance_id BIGINT, ship_type_id BIGINT, PRIMARY KEY (killmail_id, character_id)
                );
                CREATE TABLE zkill_statistics_cache (
                    character_id BIGINT PRIMARY KEY, ships_destroyed INTEGER NOT NULL, solo_kills INTEGER NOT NULL,
                    solo_ratio DOUBLE NOT NULL, avg_gang_size DOUBLE NOT NULL, ships_lost INTEGER NOT NULL,
                    solo_losses INTEGER NOT NULL, general_style TEXT NOT NULL, checked_at_utc TEXT NOT NULL,
                    no_history_marker BOOLEAN
                );
                CREATE TABLE pilot_identity_cache (
                    input_name TEXT PRIMARY KEY, character_id BIGINT, character_name TEXT, verify_status TEXT NOT NULL,
                    security_status DOUBLE, corporation_id BIGINT, corporation_name TEXT, corporation_ticker TEXT,
                    alliance_id BIGINT, alliance_name TEXT, alliance_ticker TEXT, cached_at_utc TIMESTAMP NOT NULL
                );
                CREATE TABLE zkill_activity_cache (
                    character_id BIGINT PRIMARY KEY, has_public_activity_data BOOLEAN NOT NULL, kills_week INTEGER,
                    solo_week INTEGER, last_active_utc TEXT, last_activity_type TEXT, checked_at_utc TEXT NOT NULL,
                    error TEXT, last_recent_call_utc TEXT, recent_coverage_start_utc TEXT
                );",
            )
            .unwrap();
        let recent = ago(1);
        let older = ago(3);
        let old = ago(10);
        connection
            .execute_batch(&format!(
                "INSERT INTO zkill_killmails VALUES
                    (1, 'h1', '{recent}', 30000142, NULL, 777, 587, 1, TRUE, FALSE, FALSE, '{recent}'),
                    (2, 'h2', '{older}', 30000142, NULL, 777, 670, 2, FALSE, FALSE, TRUE, '{older}'),
                    (3, 'h3', '{old}', 30000142, NULL, {TRAL}, 587, 3, FALSE, FALSE, TRUE, '{old}'),
                    (4, 'h4', '{older}', 30000142, NULL, 777, 587, 2, FALSE, FALSE, TRUE, '{older}');
                INSERT INTO zkill_killmail_attackers VALUES
                    (1, {LUKAS}, 98000001, NULL, 11567),
                    (2, {LUKAS}, 98000001, NULL, 11567),
                    (4, {LUKAS}, 98000001, NULL, 11567),
                    (4, {SYMPTOM}, 98000002, NULL, 11567);
                INSERT INTO zkill_statistics_cache VALUES
                    ({LUKAS}, 120, 30, 0.25, 4.0, 12, 2, 'Gang', '{recent}', FALSE);
                INSERT INTO pilot_identity_cache VALUES
                    ('LUKAS NAARII', {LUKAS}, 'Lukas Naarii', 'Partial', 1.5, 98000001, 'Corp One', NULL, NULL, NULL, NULL, '2026-09-20T00:00:00+00:00');
                INSERT INTO zkill_activity_cache VALUES
                    ({LUKAS}, TRUE, 1, 0, NULL, NULL, '{recent}', NULL, '{recent}', '{old}');"
            ))
            .unwrap();
        drop(connection);
        path
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

    fn response_text(pointer: *mut c_char) -> String {
        let text = unsafe { CStr::from_ptr(pointer) }.to_str().unwrap().to_string();
        killright_free_string(pointer);
        text
    }

    #[test]
    fn batch_returns_one_result_per_pilot_in_request_order() {
        let path = create_batch_database();
        let (window, style, threat) = configurations();

        let results = analyze_pilots_batch(&[SYMPTOM, LUKAS, TRAL], &path, &window, &style, &threat);

        std::fs::remove_file(&path).unwrap();

        assert_eq!(
            results.iter().map(|result| result.character_id).collect::<Vec<_>>(),
            vec![SYMPTOM, LUKAS, TRAL]
        );
        assert!(results.iter().all(|result| result.failure.is_none()));
        assert!(results.iter().all(|result| result.derived_activity.is_some()));
    }

    #[test]
    fn batch_result_equals_the_single_pilot_assembly_for_each_pilot() {
        let path = create_batch_database();
        let (window, style, threat) = configurations();
        let ids = [LUKAS, TRAL, SYMPTOM];
        let now = Utc::now();

        let batch = assemble_pilot_responses(&ids, read_pilot_inputs(&path, &ids), &window, &style, &threat, now);
        let singles = ids
            .iter()
            .map(|id| {
                assemble_pilot_responses(&[*id], read_pilot_inputs(&path, &[*id]), &window, &style, &threat, now)
                    .remove(0)
            })
            .collect::<Vec<_>>();

        std::fs::remove_file(&path).unwrap();

        for (from_batch, single) in batch.iter().zip(singles.iter()) {
            assert_eq!(
                serde_json::to_string(from_batch).unwrap(),
                serde_json::to_string(single).unwrap()
            );
        }
    }

    #[test]
    fn derived_views_carry_weekly_counts_and_newest_killmail() {
        let path = create_batch_database();
        let (window, style, threat) = configurations();

        let results = analyze_pilots_batch(&[LUKAS, TRAL, SYMPTOM], &path, &window, &style, &threat);

        std::fs::remove_file(&path).unwrap();

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
    fn unreadable_database_fails_every_pilot_without_failing_the_call() {
        let (window, style, threat) = configurations();
        let path = std::env::temp_dir().join(format!("analyze-pilots-missing-{}", unique_suffix())).join("none.duckdb");

        let results = analyze_pilots_batch(&[LUKAS, TRAL], &path, &window, &style, &threat);

        assert_eq!(results.len(), 2);
        assert!(results.iter().all(|result| result.failure.as_deref() == Some("repository_read_error:killmails")));
    }

    #[test]
    fn recorder_aggregates_across_the_batch() {
        let path = create_batch_database();
        let (window, style, threat) = configurations();
        timing_recorder::begin_forced();

        analyze_pilots_batch(&[LUKAS, TRAL, SYMPTOM], &path, &window, &style, &threat);
        let snapshot = timing_recorder::take().expect("recorder was active");

        std::fs::remove_file(&path).unwrap();

        assert_eq!(snapshot.timing_counts["pilots_analyzed"], 3);
        assert_eq!(snapshot.timing_counts["pilot_failures"], 0);
        assert!(snapshot.timings_ms.contains_key("killmails_query"));
        assert!(snapshot.timings_ms.contains_key("pilot_loop_total"));
        assert!(snapshot.timings_ms.contains_key("derived_activity"));
        assert!(snapshot.timings_ms["open_connection"] > 0.0);
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
}
