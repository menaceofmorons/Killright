use std::sync::Mutex;
use std::thread;
use std::time::{Duration, Instant};

/// Serialises request starts to at most `max_requests_per_second`, matching the
/// C# `AsyncRequestRateLimiter` in `ZkillHistoryClient.cs`: the gate is held for the
/// full wait (including the delay), not released mid-sleep, so request starts never
/// bunch up under concurrent callers.
pub struct RequestRateLimiter {
    minimum_spacing: Duration,
    next_allowed: Mutex<Instant>,
}

impl RequestRateLimiter {
    pub fn new(max_requests_per_second: u32) -> Self {
        assert!(max_requests_per_second > 0, "max_requests_per_second must be greater than zero");

        RequestRateLimiter {
            minimum_spacing: Duration::from_secs_f64(1.0 / max_requests_per_second as f64),
            next_allowed: Mutex::new(Instant::now()),
        }
    }

    pub fn wait(&self) {
        let mut next_allowed = self.next_allowed.lock().expect("rate limiter mutex poisoned");
        let now = Instant::now();

        if *next_allowed > now {
            thread::sleep(*next_allowed - now);
        }

        *next_allowed = Instant::now() + self.minimum_spacing;
    }
}
