use crate::threat_analysis::threat_configuration_models::ThreatConfiguration;

pub(crate) const THREAT_SCORE_MAXIMUM: i32 = 100;

pub(crate) fn validate_threat_configuration(
    configuration: &ThreatConfiguration,
) -> Result<(), String> {
    validate_component_weights(configuration)?;
    validate_non_negative_scores(configuration)?;
    validate_band_scores_within_component_maxima(configuration)?;
    validate_recent_activity_points(configuration)?;
    validate_security_status_bounds(configuration)?;
    validate_gating(configuration)?;

    Ok(())
}

fn validate_gating(configuration: &ThreatConfiguration) -> Result<(), String> {
    let gating = &configuration.gating;

    for (name, value) in [
        ("mediumCap", gating.medium_cap),
        ("killLossCap1", gating.kill_loss_cap_1),
        ("highCap", gating.high_cap),
        ("floor", gating.floor),
    ] {
        if !(1..=100).contains(&value) {
            return Err(format!("gating.{} {} is outside 1 to 100", name, value));
        }
    }

    if gating.kill_loss_cap_1 > gating.high_cap {
        return Err("gating.killLossCap1 must not exceed gating.highCap".to_string());
    }

    if gating.floor > gating.medium_cap {
        return Err("gating.floor must not exceed gating.mediumCap".to_string());
    }

    if gating.floor > gating.kill_loss_cap_1 {
        return Err("gating.floor must not exceed gating.killLossCap1".to_string());
    }

    if !(gating.medium_ratio_maximum > 0.0 && gating.medium_ratio_maximum < gating.high_ratio_below)
    {
        return Err(
            "gating.mediumRatioMaximum must be above 0 and below gating.highRatioBelow".to_string(),
        );
    }

    Ok(())
}

fn validate_component_weights(configuration: &ThreatConfiguration) -> Result<(), String> {
    let weights = &configuration.component_weights;

    let total = weights.historical_capability.maximum_score
        + weights.survivability.maximum_score
        + weights.loss_quality.maximum_score
        + weights.recent_activity.maximum_score
        + weights.security_status.maximum_score;

    if total != THREAT_SCORE_MAXIMUM {
        return Err(format!(
            "component weights must total {}; actual total was {}",
            THREAT_SCORE_MAXIMUM, total
        ));
    }

    Ok(())
}

fn check_score_within_maximum(label: &str, score: i32, maximum: i32) -> Result<(), String> {
    if score < 0 || score > maximum {
        return Err(format!(
            "{} score {} is outside the component maximum of {}",
            label, score, maximum
        ));
    }

    Ok(())
}

fn validate_band_scores_within_component_maxima(
    configuration: &ThreatConfiguration,
) -> Result<(), String> {
    let weights = &configuration.component_weights;

    let historical = &configuration.historical_capability;
    let maximum = weights.historical_capability.maximum_score;
    for band in &historical.kill_volume_bands {
        check_score_within_maximum("historicalCapability.killVolumeBands", band.score, maximum)?;
    }
    for band in &historical.solo_kill_bands {
        check_score_within_maximum("historicalCapability.soloKillBands", band.score, maximum)?;
    }
    check_score_within_maximum(
        "historicalCapability.soloRatio",
        historical.solo_ratio.score,
        maximum,
    )?;
    for modifier in &historical.style_modifiers {
        check_score_within_maximum(
            "historicalCapability.styleModifiers",
            modifier.score,
            maximum,
        )?;
    }

    let survivability = &configuration.survivability;
    let maximum = weights.survivability.maximum_score;
    for band in &survivability.ratios {
        check_score_within_maximum("survivability.ratios", band.score, maximum)?;
    }
    for band in &survivability.no_loss_bands {
        check_score_within_maximum("survivability.noLossBands", band.score, maximum)?;
    }

    let loss_quality = &configuration.loss_quality;
    let maximum = weights.loss_quality.maximum_score;
    for style in &loss_quality.styles {
        for band in &style.bands {
            check_score_within_maximum("lossQuality.styles", band.score, maximum)?;
        }
    }
    for band in &loss_quality.default_style.bands {
        check_score_within_maximum("lossQuality.defaultStyle", band.score, maximum)?;
    }
    check_score_within_maximum(
        "lossQuality.noLossesWithKillsScore",
        loss_quality.no_losses_with_kills_score,
        maximum,
    )?;

    Ok(())
}

fn validate_recent_activity_points(configuration: &ThreatConfiguration) -> Result<(), String> {
    let points = &configuration.recent_activity.points;

    if points.is_empty() {
        return Err("recentActivity.points must not be empty".to_string());
    }

    let maximum_score = configuration
        .component_weights
        .recent_activity
        .maximum_score as f64;
    let mut previous_rate = f64::MIN;
    let mut previous_score = f64::MIN;

    for point in points {
        if point.daily_rate <= previous_rate {
            return Err(
                "recentActivity.points must be strictly ordered by ascending dailyRate".to_string(),
            );
        }

        if point.score < previous_score {
            return Err(
                "recentActivity.points scores must be non-decreasing by dailyRate".to_string(),
            );
        }

        if point.score < 0.0 || point.score > maximum_score {
            return Err(format!(
                "recentActivity.points score {} is outside the component maximum of {}",
                point.score, maximum_score
            ));
        }

        previous_rate = point.daily_rate;
        previous_score = point.score;
    }

    Ok(())
}

fn validate_security_status_bounds(configuration: &ThreatConfiguration) -> Result<(), String> {
    let maximum_score = configuration
        .component_weights
        .security_status
        .maximum_score;

    for band in &configuration.security_status.bands {
        if band.score < 0 || band.score > maximum_score {
            return Err(format!(
                "securityStatus band score {} is outside the component maximum of {}",
                band.score, maximum_score
            ));
        }
    }

    Ok(())
}

fn validate_non_negative_scores(configuration: &ThreatConfiguration) -> Result<(), String> {
    let weights = &configuration.component_weights;

    let all_weights = [
        weights.historical_capability.maximum_score,
        weights.survivability.maximum_score,
        weights.loss_quality.maximum_score,
        weights.recent_activity.maximum_score,
        weights.security_status.maximum_score,
    ];

    if all_weights.iter().any(|score| *score < 0) {
        return Err("component weights cannot be negative".to_string());
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::threat_analysis::threat_test_fixtures::THREAT_CONFIGURATION_JSON;

    fn configuration() -> ThreatConfiguration {
        serde_json::from_str(THREAT_CONFIGURATION_JSON).expect("fixture must parse")
    }

    #[test]
    fn fixture_configuration_is_valid() {
        assert!(validate_threat_configuration(&configuration()).is_ok());
    }

    #[test]
    fn rejects_a_band_score_above_its_component_maximum() {
        let mut historical = configuration();
        historical.historical_capability.kill_volume_bands[0].score = 41;
        let mut survivability = configuration();
        survivability.survivability.ratios[0].score = 26;
        let mut loss_quality = configuration();
        loss_quality.loss_quality.default_style.bands[0].score = 21;
        let mut no_losses = configuration();
        no_losses.loss_quality.no_losses_with_kills_score = 21;
        let mut security = configuration();
        security.security_status.bands[0].score = 6;
        let mut activity = configuration();
        activity.recent_activity.points[4].score = 11.0;

        for (label, value) in [
            ("historical", historical),
            ("survivability", survivability),
            ("loss quality", loss_quality),
            ("no losses", no_losses),
            ("security", security),
            ("activity", activity),
        ] {
            assert!(validate_threat_configuration(&value).is_err(), "{label}");
        }
    }

    #[test]
    fn rejects_a_negative_band_score() {
        let mut value = configuration();
        value.survivability.no_loss_bands[2].score = -1;

        assert!(validate_threat_configuration(&value).is_err());
    }

    #[test]
    fn rejects_maxima_that_do_not_total_the_score_maximum() {
        let mut value = configuration();
        value.component_weights.security_status.maximum_score = 6;

        let error = validate_threat_configuration(&value).unwrap_err();

        assert!(error.contains("must total 100"), "{error}");
    }
}
