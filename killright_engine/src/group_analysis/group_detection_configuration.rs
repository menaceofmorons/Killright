use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct GroupDetectionConfiguration {
    #[serde(rename = "minimumSharedEvents")]
    pub minimum_shared_events: i64,
    #[serde(rename = "strengthStep")]
    pub strength_step: i32,
    #[serde(rename = "gangSizeWeights")]
    pub gang_size_weights: Vec<GangSizeWeight>,
    #[serde(rename = "sampleFactor")]
    pub sample_factor: SampleFactorConfiguration,
    #[serde(rename = "splitBonus")]
    pub split_bonus: i32,
    #[serde(rename = "chainDiscount")]
    pub chain_discount: f64,
    #[serde(rename = "chainStrengthMaximum")]
    pub chain_strength_maximum: f64,
    #[serde(rename = "intermediaryBonus")]
    pub intermediary_bonus: IntermediaryBonusConfiguration,
}

#[derive(Debug, Clone, Deserialize)]
pub struct GangSizeWeight {
    #[serde(rename = "maximumGangSize")]
    pub maximum_gang_size: i64,
    pub weight: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SampleFactorConfiguration {
    pub minimum: f64,
    pub maximum: f64,
    #[serde(rename = "saturatesAtCountedSharedKills")]
    pub saturates_at_counted_shared_kills: i64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct IntermediaryBonusConfiguration {
    #[serde(rename = "perAdditional")]
    pub per_additional: i32,
    pub maximum: i32,
}

pub(crate) fn validate_group_detection_configuration(
    configuration: &GroupDetectionConfiguration,
) -> Result<(), String> {
    validate_weights_and_factors(configuration)?;
    validate_steps_and_saturation(configuration)?;

    Ok(())
}

fn validate_weights_and_factors(configuration: &GroupDetectionConfiguration) -> Result<(), String> {
    for gang_size_weight in &configuration.gang_size_weights {
        if !(0.0..=1.0).contains(&gang_size_weight.weight) {
            return Err(format!(
                "gangSizeWeights entry for maximumGangSize {} has weight {} outside 0-1",
                gang_size_weight.maximum_gang_size, gang_size_weight.weight
            ));
        }
    }

    let sample_factor = &configuration.sample_factor;

    if !(0.0..=1.0).contains(&sample_factor.minimum)
        || !(0.0..=1.0).contains(&sample_factor.maximum)
    {
        return Err(format!(
            "sampleFactor minimum {} and maximum {} must both be within 0-1",
            sample_factor.minimum, sample_factor.maximum
        ));
    }

    if sample_factor.minimum > sample_factor.maximum {
        return Err(format!(
            "sampleFactor minimum {} cannot exceed maximum {}",
            sample_factor.minimum, sample_factor.maximum
        ));
    }

    if !(0.0..=1.0).contains(&configuration.chain_discount) {
        return Err(format!(
            "chainDiscount {} must be within 0-1",
            configuration.chain_discount
        ));
    }

    Ok(())
}

fn validate_steps_and_saturation(
    configuration: &GroupDetectionConfiguration,
) -> Result<(), String> {
    if configuration.strength_step <= 0 {
        return Err(format!(
            "strengthStep must be positive; actual value was {}",
            configuration.strength_step
        ));
    }

    if configuration
        .sample_factor
        .saturates_at_counted_shared_kills
        <= 0
    {
        return Err(format!(
            "sampleFactor.saturatesAtCountedSharedKills must be positive; actual value was {}",
            configuration
                .sample_factor
                .saturates_at_counted_shared_kills
        ));
    }

    if configuration.minimum_shared_events <= 0 {
        return Err(format!(
            "minimumSharedEvents must be positive; actual value was {}",
            configuration.minimum_shared_events
        ));
    }

    Ok(())
}
