use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct RecentWindowConfiguration {
    #[serde(rename = "recentWindowDays")]
    pub recent_window_days: i64,
}

pub(crate) fn validate_recent_window_configuration(
    configuration: &RecentWindowConfiguration,
) -> Result<(), String> {
    if configuration.recent_window_days <= 0 {
        return Err(format!(
            "recentWindowDays must be positive; actual value was {}",
            configuration.recent_window_days
        ));
    }

    Ok(())
}
