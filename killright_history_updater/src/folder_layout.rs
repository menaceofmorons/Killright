use std::fs;
use std::io;
use std::path::{Path, PathBuf};

pub const WORKING_DIRECTORY_NAME: &str = "Working";
pub const LIVE_DIRECTORY_NAME: &str = "Live";
pub const ARCHIVE_DIRECTORY_NAME: &str = "Archive";
pub const FAILED_DIRECTORY_NAME: &str = "Failed";
pub const TEST_DIRECTORY_NAME: &str = "Test";

pub fn ensure_folder_layout(root: &Path) -> io::Result<()> {
    fs::create_dir_all(working_dir(root))?;
    fs::create_dir_all(live_dir(root))?;
    fs::create_dir_all(archive_dir(root))?;
    fs::create_dir_all(failed_dir(root))?;
    fs::create_dir_all(test_dir(root))?;
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

pub fn test_dir(root: &Path) -> PathBuf {
    root.join(TEST_DIRECTORY_NAME)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::env;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn unique_temp_root(test_name: &str) -> PathBuf {
        let suffix = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        env::temp_dir().join(format!("killright-folder-layout-test-{test_name}-{suffix}"))
    }

    #[test]
    fn ensure_folder_layout_creates_all_five_subfolders_and_root() {
        let root = unique_temp_root("creates-all-five");
        assert!(!root.exists());

        ensure_folder_layout(&root).unwrap();

        assert!(working_dir(&root).is_dir());
        assert!(live_dir(&root).is_dir());
        assert!(archive_dir(&root).is_dir());
        assert!(failed_dir(&root).is_dir());
        assert!(test_dir(&root).is_dir());

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
        assert!(test_dir(&root).is_dir());

        fs::remove_dir_all(&root).unwrap();
    }
}
