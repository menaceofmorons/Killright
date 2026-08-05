use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process;

pub struct SingleInstanceLock {
    lock_path: PathBuf,
}

impl SingleInstanceLock {
    /// Attempts to acquire the single-instance lock at `lock_path`.
    ///
    /// Returns `Ok(Some(lock))` if the lock was acquired by this process.
    /// Returns `Ok(None)` if another live process already holds the lock;
    /// callers should treat this as "attach to the existing run" and stop
    /// without starting new work. A stale lock file (referencing a PID that
    /// is no longer running) is removed automatically before retrying.
    pub fn acquire(lock_path: &Path) -> io::Result<Option<SingleInstanceLock>> {
        if let Some(existing_pid) = read_lock_pid(lock_path)? {
            if is_process_running(existing_pid) {
                println!(
                    "A history update is already in progress (PID {existing_pid}). Attaching to the existing run."
                );
                return Ok(None);
            }

            println!("Found a stale lock file for PID {existing_pid}. Removing it before continuing.");
            fs::remove_file(lock_path)?;
        }

        let current_pid = process::id();
        let mut lock_file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(lock_path)?;
        write!(lock_file, "{current_pid}")?;

        Ok(Some(SingleInstanceLock {
            lock_path: lock_path.to_path_buf(),
        }))
    }
}

impl Drop for SingleInstanceLock {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.lock_path);
    }
}

fn read_lock_pid(lock_path: &Path) -> io::Result<Option<u32>> {
    if !lock_path.exists() {
        return Ok(None);
    }

    let contents = fs::read_to_string(lock_path)?;
    Ok(contents.trim().parse::<u32>().ok())
}

fn is_process_running(pid: u32) -> bool {
    let output = process::Command::new("tasklist")
        .args(["/FI", &format!("PID eq {pid}"), "/NH"])
        .output();

    match output {
        Ok(result) => {
            let stdout = String::from_utf8_lossy(&result.stdout);
            stdout.contains(&pid.to_string())
        }
        Err(_) => false,
    }
}
