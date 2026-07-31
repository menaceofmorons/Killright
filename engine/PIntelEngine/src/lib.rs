use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::sync::{Mutex, OnceLock};

#[path = "pintel_engine/mod_pintel_engine.rs"]
pub mod pintel_engine;

use pintel_engine::contracts::{
    PilotAnalysisRequest,
    PilotAnalysisResponse,
};
use pintel_engine::recent_style::{
    RecentKillmailInput,
    RecentStyleRequest,
};
use pintel_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use pintel_engine::repositories::recent_killmail_repository::RecentKillmailRepository;
use pintel_engine::shared::database_path::get_database_path;
use pintel_engine::shared::recent_style_contract::STYLE_UNKNOWN;
use std::path::PathBuf;

struct RuntimeState {
    database_path: PathBuf,
}

static RUNTIME: OnceLock<Mutex<Option<RuntimeState>>> = OnceLock::new();

#[no_mangle]
pub extern "C" fn pintel_initialize() -> i32 {
    let result = panic::catch_unwind(|| {
        let database_path = match get_database_path() {
            Ok(value) => value,
            Err(_) => return 0,
        };

        let cell = RUNTIME.get_or_init(|| Mutex::new(None));
        let mut guard = match cell.lock() {
            Ok(value) => value,
            Err(_) => return 0,
        };

        *guard = Some(RuntimeState { database_path });
        1
    });

    result.unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn pintel_analyze_pilot(
    request_json: *const c_char)
    -> *mut c_char
{
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return unknown_response(0);
        }

        let request_text = unsafe {
            CStr::from_ptr(request_json)
        };

        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return unknown_response(0),
        };

        let request = match serde_json::from_str::<PilotAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return unknown_response(0),
        };

        let database_path = match get_runtime_database_path() {
            Some(value) => value,
            None => return unknown_response(request.character_id),
        };

        let recent_killmail_repository =
            RecentKillmailRepository::new(database_path);

        let killmails = recent_killmail_repository
            .get_for_character(request.character_id)
            .unwrap_or_default()
            .into_iter()
            .map(|row| RecentKillmailInput {
                killmail_id: row.killmail_id,
                is_loss: row.is_loss,
                attacker_count: row.attacker_count,
                is_solo: row.is_solo,
                ship_type_id: row.ship_type_id,
            })
            .collect::<Vec<_>>();

        let recent_style_result = analyze_recent_style(
            RecentStyleRequest {
                character_id: request.character_id,
                killmails,
            });

        response_json(
            PilotAnalysisResponse {
                character_id: request.character_id,
                recent_style: Some(recent_style_result.recent_style),
            })
    });

    result.unwrap_or_else(|_| unknown_response(0))
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
pub extern "C" fn pintel_free_string(
    value: *mut c_char)
{
    if value.is_null() {
        return;
    }

    unsafe {
        let _ = CString::from_raw(value);
    }
}

fn get_runtime_database_path() -> Option<PathBuf> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some(runtime.database_path.clone())
}

fn unknown_response(character_id: i64) -> *mut c_char {
    response_json(
        PilotAnalysisResponse {
            character_id,
            recent_style: Some(STYLE_UNKNOWN.to_string()),
        })
}

fn response_json(
    response: PilotAnalysisResponse)
    -> *mut c_char
{
    let json = serde_json::to_string(&response)
        .unwrap_or_else(|_| {
            "{\"character_id\":0,\"recent_style\":\"Unknown\"}".to_string()
        });

    CString::new(json)
        .unwrap_or_else(|_| {
            CString::new("{\"character_id\":0,\"recent_style\":\"Unknown\"}").unwrap()
        })
        .into_raw()
}