use std::cell::RefCell;
use std::collections::BTreeMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use serde::Deserialize;

#[derive(Debug, Clone, Default, Deserialize)]
pub struct TimingConfiguration {
    #[serde(default)]
    pub enabled: bool,
}

#[derive(Debug, Default)]
struct Recorder {
    phases_ms: BTreeMap<String, f64>,
    counts: BTreeMap<String, i64>,
}

pub struct TimingSnapshot {
    pub timings_ms: BTreeMap<String, f64>,
    pub timing_counts: BTreeMap<String, i64>,
}

static ENABLED: AtomicBool = AtomicBool::new(false);

thread_local! {
    static RECORDER: RefCell<Option<Recorder>> = const { RefCell::new(None) };
}

pub fn set_enabled(enabled: bool) {
    ENABLED.store(enabled, Ordering::SeqCst);
}

pub fn is_enabled() -> bool {
    ENABLED.load(Ordering::SeqCst)
}

pub fn begin() -> Option<Instant> {
    if is_enabled() {
        RECORDER.with(|cell| *cell.borrow_mut() = Some(Recorder::default()));
        Some(Instant::now())
    } else {
        clear();
        None
    }
}

pub fn clear() {
    RECORDER.with(|cell| *cell.borrow_mut() = None);
}

pub fn is_active() -> bool {
    RECORDER.with(|cell| cell.borrow().is_some())
}

pub fn record_ms(phase: &str, milliseconds: f64) {
    RECORDER.with(|cell| {
        if let Some(recorder) = cell.borrow_mut().as_mut() {
            *recorder.phases_ms.entry(phase.to_string()).or_insert(0.0) += milliseconds;
        }
    });
}

pub fn add_count(counter: &str, value: i64) {
    RECORDER.with(|cell| {
        if let Some(recorder) = cell.borrow_mut().as_mut() {
            *recorder.counts.entry(counter.to_string()).or_insert(0) += value;
        }
    });
}

pub fn take() -> Option<TimingSnapshot> {
    RECORDER.with(|cell| {
        cell.borrow_mut().take().map(|recorder| TimingSnapshot {
            timings_ms: recorder.phases_ms,
            timing_counts: recorder.counts,
        })
    })
}

pub struct PhaseGuard {
    phase: &'static str,
    started: Option<Instant>,
}

pub fn scope(phase: &'static str) -> PhaseGuard {
    PhaseGuard {
        phase,
        started: if is_active() { Some(Instant::now()) } else { None },
    }
}

impl Drop for PhaseGuard {
    fn drop(&mut self) {
        if let Some(started) = self.started {
            record_ms(self.phase, started.elapsed().as_secs_f64() * 1000.0);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn inactive_recorder_is_a_no_op() {
        clear();

        {
            let _guard = scope("phase_a");
        }
        add_count("counter_a", 3);
        record_ms("phase_b", 1.0);

        assert!(!is_active());
        assert!(take().is_none());
    }

    #[test]
    fn active_recorder_accumulates_a_repeated_phase() {
        RECORDER.with(|cell| *cell.borrow_mut() = Some(Recorder::default()));

        for _ in 0..3 {
            let _guard = scope("repeated");
            std::thread::sleep(std::time::Duration::from_millis(2));
        }
        add_count("rows", 2);
        add_count("rows", 5);
        let snapshot = take().expect("recorder was active");

        assert!(snapshot.timings_ms["repeated"] >= 6.0);
        assert_eq!(snapshot.timing_counts["rows"], 7);
        assert!(!is_active());
    }

    #[test]
    fn begin_starts_a_recorder_only_when_enabled() {
        set_enabled(false);
        assert!(begin().is_none());
        assert!(!is_active());

        set_enabled(true);
        assert!(begin().is_some());
        assert!(is_active());

        set_enabled(false);
        assert!(begin().is_none());
        assert!(!is_active());
    }
}
