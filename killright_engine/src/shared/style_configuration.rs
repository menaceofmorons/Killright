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
}

pub(crate) fn validate_style_configuration(configuration: &StyleConfiguration) -> Result<(), String> {
    if configuration.blob_minimum_average_attackers <= 0.0 {
        return Err(format!(
            "blobMinimumAverageAttackers must be positive; actual value was {}",
            configuration.blob_minimum_average_attackers
        ));
    }

    if configuration.fleet_minimum_average_attackers <= configuration.blob_minimum_average_attackers {
        return Err(format!(
            "fleetMinimumAverageAttackers {} must exceed blobMinimumAverageAttackers {}",
            configuration.fleet_minimum_average_attackers, configuration.blob_minimum_average_attackers
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
            podder_minimum_share_percent: 20.0,
            podder_minimum_kill_count: 5,
        };

        assert!(validate_style_configuration(&configuration).is_ok());
    }

    #[test]
    fn validate_style_configuration_rejects_non_positive_blob_minimum() {
        let configuration = StyleConfiguration {
            blob_minimum_average_attackers: 0.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 20.0,
            podder_minimum_kill_count: 5,
        };

        assert!(validate_style_configuration(&configuration).is_err());
    }

    #[test]
    fn validate_style_configuration_rejects_fleet_not_exceeding_blob() {
        let configuration = StyleConfiguration {
            blob_minimum_average_attackers: 11.0,
            fleet_minimum_average_attackers: 11.0,
            podder_minimum_share_percent: 20.0,
            podder_minimum_kill_count: 5,
        };

        assert!(validate_style_configuration(&configuration).is_err());
    }
}
