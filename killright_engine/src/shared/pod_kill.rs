pub const CAPSULE_SHIP_TYPE_ID: i64 = 670;
pub const CAPSULE_GENOLUTION_SHIP_TYPE_ID: i64 = 33328;

pub fn is_pod_kill(victim_ship_type_id: Option<i64>) -> bool {
    matches!(
        victim_ship_type_id,
        Some(CAPSULE_SHIP_TYPE_ID) | Some(CAPSULE_GENOLUTION_SHIP_TYPE_ID)
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_pod_kill_true_for_capsule() {
        assert!(is_pod_kill(Some(CAPSULE_SHIP_TYPE_ID)));
    }

    #[test]
    fn is_pod_kill_true_for_capsule_genolution() {
        assert!(is_pod_kill(Some(CAPSULE_GENOLUTION_SHIP_TYPE_ID)));
    }

    #[test]
    fn is_pod_kill_false_for_other_ship_or_none() {
        assert!(!is_pod_kill(Some(587)));
        assert!(!is_pod_kill(None));
    }
}
