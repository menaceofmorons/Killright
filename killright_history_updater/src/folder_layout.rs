use std::fs;
use std::io;
use std::path::{Path, PathBuf};

/// Subfolder names under the historic database root introduced by Step
/// 19.00.55 (Design Specification v5.4 Section 6.9.2/4.7; the plan's
/// 19.00.55 section). `Working` holds the in-progress import database this
/// crate builds (this step); `Live`, `Archive`, and `Failed` are
/// destinations for later steps -- the promotion copy at 19.00.56,
/// archive-on-swap at 19.00.60, and failed-build handling at 19.00.58 --
/// and are created here only so every later step's "first run" behaviour is
/// exercised against real, already-existing directories rather than each
/// step separately handling directory creation for a folder an earlier step
/// should have made.
pub const WORKING_DIRECTORY_NAME: &str = "Working";
pub const LIVE_DIRECTORY_NAME: &str = "Live";
pub const ARCHIVE_DIRECTORY_NAME: &str = "Archive";
pub const FAILED_DIRECTORY_NAME: &str = "Failed";

/// Ensures all four subfolders exist under `root` (the historic database
/// root, e.g. `%LOCALAPPDATA%\KillRight\HistoryUpdater`), creating any that
/// are missing along with `root` itself if needed. Safe to call on every
/// `build_staging` invocation -- `fs::create_dir_all` is a no-op when the
/// directory already exists.
pub fn ensure_folder_layout(root: &Path) -> io::Result<()> {
    fs::create_dir_all(working_dir(root))?;
    fs::create_dir_all(live_dir(root))?;
    fs::create_dir_all(archive_dir(root))?;
    fs::create_dir_all(failed_dir(root))?;
    Ok(())
}

pub fn working_dir(root: &Path) -> PathBuf {
    root.join(WORKING_DIRECTORY_NAME)
}

pub fn live_dir(root: &Path) -> PathBuf {
    root.join(LIVE_DIRECTORY_NAME)
}

pub fn archive_dir(root: &Path) -> PathBuf {
    root.join(ARCHIVE_DIRECTORY_NAME)
}

pub fn failed_dir(root: &Path) -> PathBuf {
    root.join(FAILED_DIRECTORY_NAME)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::env;
    use std::time::{SystemTime, UNIX_EPOCH};

    /// Section 6.4 Agent Test Independence: a unique, nanosecond-suffixed
    /// temp directory per test, never shared between tests and never
    /// dependent on execution order.
    fn unique_temp_root(test_name: &str) -> PathBuf {
        let suffix = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        env::temp_dir().join(format!("killright-folder-layout-test-{test_name}-{suffix}"))
    }

    #[test]
    fn ensure_folder_layout_creates_all_four_subfolders_and_root() {
        let root = unique_temp_root("creates-all-four");
        assert!(!root.exists());

        ensure_folder_layout(&root).unwrap();

        assert!(working_dir(&root).is_dir());
        assert!(live_dir(&root).is_dir());
        assert!(archive_dir(&root).is_dir());
        assert!(failed_dir(&root).is_dir());

        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn ensure_folder_layout_is_idempotent_when_already_present() {
        let root = unique_temp_root("idempotent");
        ensure_folder_layout(&root).unwrap();

        ensure_folder_layout(&root).unwrap();

        assert!(working_dir(&root).is_dir());
        assert!(live_dir(&root).is_dir());
        assert!(archive_dir(&root).is_dir());
        assert!(failed_dir(&root).is_dir());

        fs::remove_dir_all(&root).unwrap();
    }
}
