$ErrorActionPreference = "Stop"

$root = "C:\Users\TomMulledy\OneDrive\Documents\Code\PilotIntel\Code"
$engineRoot = Join-Path $root "engine\killright_engine"
$srcRoot = Join-Path $engineRoot "src"
$oldModuleRoot = Join-Path $srcRoot "pintel_engine"

Set-Location $root

function Assert-ProcessClosed {
    param([string]$Name)

    $process = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        throw "Close $Name before running this guide. The process is still running."
    }
}

function Move-DirectoryIfNeeded {
    param(
        [string]$OldPath,
        [string]$NewPath
    )

    if (Test-Path $NewPath) {
        Write-Host "Already exists: $NewPath"
        return
    }

    if (!(Test-Path $OldPath)) {
        throw "Expected directory not found: $OldPath"
    }

    Move-Item -Path $OldPath -Destination $NewPath
}

function Move-FileIfNeeded {
    param(
        [string]$OldPath,
        [string]$NewPath
    )

    if (Test-Path $NewPath) {
        Write-Host "Already exists: $NewPath"
        return
    }

    if (!(Test-Path $OldPath)) {
        throw "Expected file not found: $OldPath"
    }

    Move-Item -Path $OldPath -Destination $NewPath
}

function Replace-InFile {
    param(
        [string]$Path,
        [string]$OldValue,
        [string]$NewValue
    )

    if (!(Test-Path $Path)) {
        throw "Expected file not found: $Path"
    }

    $content = Get-Content -Path $Path -Raw
    $updated = $content.Replace($OldValue, $NewValue)
    if ($updated -ne $content) {
        Set-Content -Path $Path -Value $updated -NoNewline
    }
}

function Replace-InRustFiles {
    param(
        [string]$OldValue,
        [string]$NewValue
    )

    Get-ChildItem -Path $srcRoot -Recurse -File -Include *.rs |
        Where-Object { $_.FullName -notmatch "\\(target|\.git|\.idea)\\" } |
        ForEach-Object {
            Replace-InFile -Path $_.FullName -OldValue $OldValue -NewValue $NewValue
        }
}

Assert-ProcessClosed -Name "rider"
Assert-ProcessClosed -Name "rustrover"

if (!(Test-Path $engineRoot)) {
    throw "Expected engine root not found: $engineRoot"
}

if (!(Test-Path $srcRoot)) {
    throw "Expected source root not found: $srcRoot"
}

if (!(Test-Path $oldModuleRoot)) {
    throw "Expected old module root not found: $oldModuleRoot"
}

Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "contracts") -NewPath (Join-Path $srcRoot "contracts")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "fleet_analysis") -NewPath (Join-Path $srcRoot "fleet_analysis")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "group_analysis") -NewPath (Join-Path $srcRoot "group_analysis")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "recent_style") -NewPath (Join-Path $srcRoot "recent_style")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "repositories") -NewPath (Join-Path $srcRoot "repositories")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "shared") -NewPath (Join-Path $srcRoot "shared")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "ship_library") -NewPath (Join-Path $srcRoot "ship_library")
Move-DirectoryIfNeeded -OldPath (Join-Path $oldModuleRoot "threat_analysis") -NewPath (Join-Path $srcRoot "threat_analysis")

Move-FileIfNeeded -OldPath (Join-Path $oldModuleRoot "mod_pintel_engine.rs") -NewPath (Join-Path $srcRoot "kr_engine.rs")

if (Test-Path $oldModuleRoot) {
    Remove-Item -Path $oldModuleRoot -Recurse -Force
}

if (Test-Path (Join-Path $engineRoot "config")) {
    Move-DirectoryIfNeeded -OldPath (Join-Path $engineRoot "config") -NewPath (Join-Path $srcRoot "config")
}

$libRs = @'
use chrono::{DateTime, Duration, Utc};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
#[path = "kr_engine.rs"]
pub mod kr_engine;
pub use kr_engine::*;
use kr_engine::contracts::{PilotAnalysisRequest, PilotAnalysisResponse};
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::repositories::pilot_identity_repository::PilotIdentityRepository;
use kr_engine::repositories::recent_killmail_repository::RecentKillmailRepository;
use kr_engine::repositories::zkill_statistics_repository::ZKillStatisticsRepository;
use kr_engine::shared::database_path::get_database_path;
use kr_engine::shared::recent_style_contract::{STYLE_INACTIVE, STYLE_UNKNOWN};
use kr_engine::threat_analysis::{
    analyze_intrinsic_threat,
    load_default_threat_configuration,
    ThreatAnalysisResponse,
    ThreatConfiguration,
};
struct RuntimeState {
    database_path: PathBuf,
    threat_configuration: ThreatConfiguration,
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
        let cell = RUNTIME.get_or_init(|| Mutex::new(None));
        let mut guard = match cell.lock() {
            Ok(value) => value,
            Err(_) => return 0,
        };
        *guard = Some(RuntimeState {
            database_path,
            threat_configuration,
        });
        1
    });
    result.unwrap_or(0)
}
#[no_mangle]
pub extern "C" fn pintel_analyze_pilot(request_json: *const c_char) -> *mut c_char {
    let result = panic::catch_unwind(|| {
        if request_json.is_null() {
            return unknown_response(0);
        }
        let request_text = unsafe { CStr::from_ptr(request_json) };
        let request_text = match request_text.to_str() {
            Ok(value) => value,
            Err(_) => return unknown_response(0),
        };
        let request = match serde_json::from_str::<PilotAnalysisRequest>(request_text) {
            Ok(value) => value,
            Err(_) => return unknown_response(0),
        };
        let Some((database_path, threat_configuration)) = get_runtime_state() else {
            return unknown_response(request.character_id);
        };
        let recent_killmail_repository = RecentKillmailRepository::new(database_path.clone());
        let zkill_statistics_repository = ZKillStatisticsRepository::new(database_path.clone());
        let pilot_identity_repository = PilotIdentityRepository::new(database_path);
        let all_killmails = recent_killmail_repository.get_for_character(request.character_id).unwrap_or_default();
        let cutoff = Utc::now() - Duration::days(7);
        let recent_window_killmails = all_killmails.iter().filter(|row| {
            DateTime::parse_from_rfc3339(&row.kill_time_utc)
                .map(|timestamp| timestamp.with_timezone(&Utc) >= cutoff)
                .unwrap_or(false)
        }).cloned().collect::<Vec<_>>();
        let statistics = zkill_statistics_repository.get_for_character(request.character_id).ok().flatten();
        let identity = pilot_identity_repository.get_for_character(request.character_id).ok().flatten();
        let killmails = recent_window_killmails.iter().map(|row| RecentKillmailInput {
            killmail_id: row.killmail_id,
            is_loss: row.is_loss,
            attacker_count: row.attacker_count,
            is_solo: row.is_solo,
            ship_type_id: row.ship_type_id,
        }).collect::<Vec<_>>();
        let recent_style = if all_killmails.is_empty() {
            STYLE_UNKNOWN.to_string()
        } else if recent_window_killmails.is_empty() {
            STYLE_INACTIVE.to_string()
        } else {
            analyze_recent_style(RecentStyleRequest { character_id: request.character_id, killmails }).recent_style
        };
        let threat = analyze_intrinsic_threat(&threat_configuration, statistics.as_ref(), &recent_window_killmails, identity.as_ref());
        response_json(PilotAnalysisResponse {
            character_id: request.character_id,
            recent_style: Some(recent_style),
            threat: Some(threat),
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
pub extern "C" fn pintel_free_string(value: *mut c_char) {
    if value.is_null() {
        return;
    }
    unsafe {
        let _ = CString::from_raw(value);
    }
}
fn get_runtime_state() -> Option<(PathBuf, ThreatConfiguration)> {
    let cell = RUNTIME.get()?;
    let guard = cell.lock().ok()?;
    let runtime = guard.as_ref()?;
    Some((runtime.database_path.clone(), runtime.threat_configuration.clone()))
}
fn unknown_response(character_id: i64) -> *mut c_char {
    response_json(PilotAnalysisResponse {
        character_id,
        recent_style: Some(STYLE_UNKNOWN.to_string()),
        threat: Some(ThreatAnalysisResponse {
            score: 0,
            band: "Unknown".to_string(),
            confidence: "Low".to_string(),
        }),
    })
}
fn response_json(response: PilotAnalysisResponse) -> *mut c_char {
    let json = serde_json::to_string(&response).unwrap_or_else(|_| {
        "{\"character_id\":0,\"recent_style\":\"Unknown\",\"threat\":{\"score\":0,\"band\":\"Unknown\",\"confidence\":\"Low\"}}".to_string()
    });
    CString::new(json).unwrap_or_else(|_| {
        CString::new("{\"character_id\":0,\"recent_style\":\"Unknown\",\"threat\":{\"score\":0,\"band\":\"Unknown\",\"confidence\":\"Low\"}}").unwrap()
    }).into_raw()
}
'@

$mainRs = @'
use std::io::{self, Read};
#[path = "kr_engine.rs"]
mod kr_engine;
pub use kr_engine::*;
use kr_engine::recent_style::{RecentKillmailInput, RecentStyleRequest};
use kr_engine::recent_style::recent_style_analyzer::analyze_recent_style;
fn main() {
    let mut input = String::new();
    io::stdin().read_to_string(&mut input).expect("failed to read stdin");
    if input.trim().is_empty() {
        return;
    }
    let request: RecentStyleRequest = serde_json::from_str(&input).expect("failed to parse analysis request");
    let result = analyze_recent_style(request);
    let output = serde_json::to_string(&result).expect("failed to serialize analysis result");
    println!("{}", output);
}
'@

Set-Content -Path (Join-Path $srcRoot "lib.rs") -Value $libRs -NoNewline
Set-Content -Path (Join-Path $srcRoot "main.rs") -Value $mainRs -NoNewline

Replace-InRustFiles -OldValue "crate::pintel_engine::" -NewValue "crate::"
Replace-InRustFiles -OldValue "pintel_engine::" -NewValue "kr_engine::"
Replace-InRustFiles -OldValue "pintel_engine/mod_pintel_engine.rs" -NewValue "kr_engine.rs"
Replace-InRustFiles -OldValue "mod_pintel_engine.rs" -NewValue "kr_engine.rs"

Set-Location $engineRoot
cargo fmt
cargo build
Set-Location $root

Write-Host "18.20.00 Rust Flattening completed."