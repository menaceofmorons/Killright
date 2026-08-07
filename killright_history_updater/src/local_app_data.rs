use std::env;

/// Resolves the root directory `killright_history_updater` uses for every
/// `%LOCALAPPDATA%\KillRight\...` path it reads or writes: the staging
/// directory and lock file (`database_path.rs`), and the live-config/status
/// files (`live_config.rs`). Checks `KILLRIGHT_LOCALAPPDATA_OVERRIDE` first,
/// purely so `HistoryUpdaterProcessLauncherIntegrationTests` can point a
/// real, end-to-end launch of this executable at an isolated temp directory
/// instead of a developer's live interactive staging state (Step
/// CC-07.08.26.02). Every real invocation -- the Developer window, a plain
/// CLI run -- has this variable unset and resolves exactly as before: the
/// OS's `LOCALAPPDATA`.
pub fn resolve_local_app_data_root() -> String {
    resolve_local_app_data_root_from(
        env::var("KILLRIGHT_LOCALAPPDATA_OVERRIDE").ok(),
        env::var("LOCALAPPDATA").ok(),
    )
}

fn resolve_local_app_data_root_from(override_value: Option<String>, local_app_data: Option<String>) -> String {
    override_value
        .or(local_app_data)
        .expect("Neither KILLRIGHT_LOCALAPPDATA_OVERRIDE nor LOCALAPPDATA environment variable is set")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resolve_local_app_data_root_from_prefers_override_when_present() {
        let resolved = resolve_local_app_data_root_from(
            Some("C:\\temp\\killright-test-override".to_string()),
            Some("C:\\Users\\someone\\AppData\\Local".to_string()),
        );

        assert_eq!(resolved, "C:\\temp\\killright-test-override");
    }

    #[test]
    fn resolve_local_app_data_root_from_falls_back_to_local_app_data_when_override_absent() {
        let resolved = resolve_local_app_data_root_from(None, Some("C:\\Users\\someone\\AppData\\Local".to_string()));

        assert_eq!(resolved, "C:\\Users\\someone\\AppData\\Local");
    }

    #[test]
    #[should_panic(expected = "Neither KILLRIGHT_LOCALAPPDATA_OVERRIDE nor LOCALAPPDATA")]
    fn resolve_local_app_data_root_from_panics_when_both_absent() {
        resolve_local_app_data_root_from(None, None);
    }
}
