use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct ThreatConfiguration {
    pub version: String,
    #[serde(rename = "lastUpdated")]
    pub last_updated: String,
    pub bands: Vec<ThreatBandConfiguration>,
    #[serde(rename = "componentWeights")]
    pub component_weights: ThreatComponentWeights,
    #[serde(rename = "historicalCapability")]
    pub historical_capability: HistoricalCapabilityConfiguration,
    pub survivability: SurvivabilityConfiguration,
    #[serde(rename = "lossQuality")]
    pub loss_quality: LossQualityConfiguration,
    #[serde(rename = "recentActivity")]
    pub recent_activity: RecentActivityConfiguration,
    #[serde(rename = "securityStatus")]
    pub security_status: SecurityStatusConfiguration,
    pub confidence: ConfidenceConfiguration,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ThreatBandConfiguration {
    pub name: String,
    #[serde(rename = "minimumScore")]
    pub minimum_score: i32,
    #[serde(rename = "maximumScore")]
    pub maximum_score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ThreatComponentWeights {
    #[serde(rename = "historicalCapability")]
    pub historical_capability: ComponentWeight,
    pub survivability: ComponentWeight,
    #[serde(rename = "lossQuality")]
    pub loss_quality: ComponentWeight,
    #[serde(rename = "recentActivity")]
    pub recent_activity: ComponentWeight,
    #[serde(rename = "securityStatus")]
    pub security_status: ComponentWeight,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ComponentWeight {
    #[serde(rename = "maximumScore")]
    pub maximum_score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct HistoricalCapabilityConfiguration {
    #[serde(rename = "killVolumeBands")]
    pub kill_volume_bands: Vec<KillVolumeBand>,
    #[serde(rename = "soloKillBands")]
    pub solo_kill_bands: Vec<SoloKillBand>,
    #[serde(rename = "soloRatio")]
    pub solo_ratio: SoloRatioRule,
    #[serde(rename = "styleModifiers")]
    pub style_modifiers: Vec<StyleModifier>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct KillVolumeBand {
    #[serde(rename = "maximumKills")]
    pub maximum_kills: i32,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SoloKillBand {
    #[serde(rename = "maximumSoloKills")]
    pub maximum_solo_kills: i32,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SoloRatioRule {
    #[serde(rename = "minimumRatio")]
    pub minimum_ratio: f64,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct StyleModifier {
    pub style: String,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SurvivabilityConfiguration {
    pub ratios: Vec<RatioBand>,
    #[serde(rename = "noLossBands")]
    pub no_loss_bands: Vec<NoLossBand>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RatioBand {
    #[serde(rename = "maximumRatio")]
    pub maximum_ratio: f64,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct NoLossBand {
    #[serde(rename = "minimumKills")]
    pub minimum_kills: i32,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct LossQualityConfiguration {
    pub styles: Vec<LossQualityStyle>,
    #[serde(rename = "defaultStyle")]
    pub default_style: LossQualityDefaultStyle,
    #[serde(rename = "noLossesWithKillsScore")]
    pub no_losses_with_kills_score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct LossQualityStyle {
    pub style: String,
    pub bands: Vec<LossQualityBand>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct LossQualityDefaultStyle {
    pub bands: Vec<LossQualityBand>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct LossQualityBand {
    #[serde(rename = "minimumSoloLossRatio")]
    pub minimum_solo_loss_ratio: f64,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RecentActivityConfiguration {
    pub bands: Vec<RecentActivityBand>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RecentActivityBand {
    #[serde(rename = "maximumRecentKills")]
    pub maximum_recent_kills: usize,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SecurityStatusConfiguration {
    pub bands: Vec<SecurityStatusBand>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SecurityStatusBand {
    #[serde(rename = "minimumSecurityStatus")]
    pub minimum_security_status: f64,
    pub score: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ConfidenceConfiguration {
    pub low: ConfidenceBand,
    pub medium: ConfidenceBand,
    pub high: HighConfidenceBand,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ConfidenceBand {
    #[serde(rename = "minimumHistoricalVolume")]
    pub minimum_historical_volume: i32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct HighConfidenceBand {
    #[serde(rename = "minimumHistoricalVolume")]
    pub minimum_historical_volume: i32,
    #[serde(rename = "requiresRecentActivity")]
    pub requires_recent_activity: bool,
}
