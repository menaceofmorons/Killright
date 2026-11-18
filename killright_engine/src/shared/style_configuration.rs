use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct StyleConfiguration {
    #[serde(rename = "blobMinimumAverageAttackers")]
    pub blob_minimum_average_attackers: f64,
    #[serde(rename = "fleetMinimumAverageAttackers")]
    pub fleet_minimum_average_attackers: f64,
    #[serde(rename = "podderMinimumSharePercent")]
    pub podder_minimum_share_percent: f64,
    #[serde(rename = "podderMinimumKillCount")]
    pub podder_minimum_kill_count: i64,
    #[serde(rename = "recentSoloSharePercent")]
    pub recent_solo_share_percent: f64,
}

pub(crate) fn validate_style_configuration(
    configuration: &StyleConfiguration,
) -> Result<(), String> {
    if configuration.blob_minimum_average_attackers <= 0.0 {
        return Err(format!(
            "blobMinimumAverageAttackers must be positive; actual value was {}",
            configuration.blob_minimum_average_attackers
        ));
    }

    if configuration.fleet_minimum_average_attackers <= configuration.blob_minimum_average_attackers
    {
        return Err(format!(
            "fleetMinimumAverageAttackers {} must exceed blobMinimumAverageAttackers {}",
            configuration.fleet_minimum_average_attackers,
            configuration.blob_minimum_average_attackers
        ));
    }

    if !(configuration.recent_solo_share_percent > 0.0
        && configuration.recent_solo_share_percent <= 100.0)
    {
        return Err(format!(
            "recentSoloSharePercent must be above 0 and at most 100; actual value was {}",
            configuration.recent_solo_share_percent
        ));
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validate_style_configuration_accepts_defaults() {
        let configuration = StyleConfiguration {
            blob_minimum_average_attackers: 5.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
            recent_solo_share_percent: 60.0,
        };

        assert!(validate_style_configuration(&configuration).is_ok());
    }

    #[test]
    fn validate_style_configuration_rejects_non_positive_blob_minimum() {
        let configuration = StyleConfiguration {
            blob_minimum_average_attackers: 0.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
            recent_solo_share_percent: 60.0,
        };

        assert!(validate_style_configuration(&configuration).is_err());
    }

    #[test]
    fn validate_style_configuration_rejects_recent_solo_share_outside_range() {
        for share in [0.0, -1.0, 100.5, f64::NAN] {
            let configuration = StyleConfiguration {
                blob_minimum_average_attackers: 5.0,
                fleet_minimum_average_attackers: 11.0,
                podder_minimum_share_percent: 35.0,
                podder_minimum_kill_count: 5,
                recent_solo_share_percent: share,
            };

            assert!(
                validate_style_configuration(&configuration).is_err(),
                "{share}"
            );
        }
    }

    #[test]
    fn validate_style_configuration_rejects_fleet_not_exceeding_blob() {
        let configuration = StyleConfiguration {
            blob_minimum_average_attackers: 11.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 35.0,
            podder_minimum_kill_count: 5,
            recent_solo_share_percent: 60.0,
        };

        assert!(validate_style_configuration(&configuration).is_err());
    }
}
