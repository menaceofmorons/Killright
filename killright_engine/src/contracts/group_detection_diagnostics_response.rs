use serde::Serialize;

use crate::group_analysis::group_detection_diagnostics::{
    AfterSplitBranch, ChainedRelationshipDiagnostic, ContributingKillmail, DirectRelationshipDiagnostic,
    GroupDetectionDiagnostics, IntermediaryHubSummary,
};

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct GroupDetectionDiagnosticsEnvelope {
    pub character_id: i64,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub diagnostics: Option<GroupDetectionDiagnosticsResponse>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub failure: Option<String>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct GroupDetectionDiagnosticsResponse {
    pub direct_relationships: Vec<DirectRelationshipDiagnosticResponse>,
    pub chains: Vec<ChainedRelationshipDiagnosticResponse>,
    pub hubs: Vec<IntermediaryHubSummaryResponse>,
    pub direct_analysis_duration_ms: i64,
    pub chain_analysis_duration_ms: i64,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ContributingKillmailResponse {
    pub killmail_id: i64,
    pub kill_time_utc: String,
    pub is_same_corporation_or_alliance: bool,
    pub unique_attacker_count: i64,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct DirectRelationshipDiagnosticResponse {
    pub pilot_a_character_id: i64,
    pub pilot_b_character_id: i64,
    pub gated_by_current_membership: bool,
    pub after_split_branch: String,
    pub contributing_killmails: Vec<ContributingKillmailResponse>,
    pub counted_shared_kills: i64,
    pub split_bonus_applied: bool,
    pub qualifies: bool,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub last_counted_kill_time_utc: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub gang_quality: Option<f64>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub sample_factor: Option<f64>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub strength: Option<i32>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub confidence: Option<i32>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ChainedRelationshipDiagnosticResponse {
    pub pilot_a_character_id: i64,
    pub pilot_c_character_id: i64,
    pub intermediary_pilot_character_id: i64,
    pub intermediary_in_scan: bool,
    pub link_ab_counted_shared_kills: i64,
    pub link_ab_most_recent_in_window_kill_time_utc: String,
    pub link_ab_split_bonus_applied: bool,
    pub link_ab_strength: i32,
    pub link_ab_confidence: i32,
    pub link_cb_counted_shared_kills: i64,
    pub link_cb_most_recent_in_window_kill_time_utc: String,
    pub link_cb_split_bonus_applied: bool,
    pub link_cb_strength: i32,
    pub link_cb_confidence: i32,
    pub chain_age_days: f64,
    pub chain_strength_for_this_intermediary: i32,
    pub is_strongest_intermediary_for_pair: bool,
    pub pair_distinct_intermediary_count: i64,
    pub pair_intermediary_bonus: i32,
    pub pair_chain_confidence: i32,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct IntermediaryHubSummaryResponse {
    pub intermediary_pilot_character_id: i64,
    pub intermediary_in_scan: bool,
    pub scanned_pilots_linked_count: i64,
}

impl From<GroupDetectionDiagnostics> for GroupDetectionDiagnosticsResponse {
    fn from(diagnostics: GroupDetectionDiagnostics) -> Self {
        Self {
            direct_relationships: diagnostics.direct_relationships.into_iter().map(Into::into).collect(),
            chains: diagnostics.chains.into_iter().map(Into::into).collect(),
            hubs: diagnostics.hubs.into_iter().map(Into::into).collect(),
            direct_analysis_duration_ms: diagnostics.direct_analysis_duration_ms,
            chain_analysis_duration_ms: diagnostics.chain_analysis_duration_ms,
        }
    }
}

impl From<ContributingKillmail> for ContributingKillmailResponse {
    fn from(value: ContributingKillmail) -> Self {
        Self {
            killmail_id: value.killmail_id,
            kill_time_utc: value.kill_time_utc,
            is_same_corporation_or_alliance: value.is_same_corporation_or_alliance,
            unique_attacker_count: value.unique_attacker_count,
        }
    }
}

impl From<DirectRelationshipDiagnostic> for DirectRelationshipDiagnosticResponse {
    fn from(value: DirectRelationshipDiagnostic) -> Self {
        Self {
            pilot_a_character_id: value.pilot_a,
            pilot_b_character_id: value.pilot_b,
            gated_by_current_membership: value.gated_by_current_membership,
            after_split_branch: match value.after_split_branch {
                AfterSplitBranch::AllCountedSinceSplit => "AllCountedSinceSplit".to_string(),
                AfterSplitBranch::NotSameOnly => "NotSameOnly".to_string(),
            },
            contributing_killmails: value.contributing_killmails.into_iter().map(Into::into).collect(),
            counted_shared_kills: value.counted_shared_kills,
            split_bonus_applied: value.split_bonus_applied,
            qualifies: value.qualifies,
            last_counted_kill_time_utc: value.last_counted_kill_time_utc,
            gang_quality: value.gang_quality,
            sample_factor: value.sample_factor,
            strength: value.strength,
            confidence: value.confidence,
        }
    }
}

impl From<ChainedRelationshipDiagnostic> for ChainedRelationshipDiagnosticResponse {
    fn from(value: ChainedRelationshipDiagnostic) -> Self {
        Self {
            pilot_a_character_id: value.pilot_a,
            pilot_c_character_id: value.pilot_c,
            intermediary_pilot_character_id: value.intermediary_pilot_b,
            intermediary_in_scan: value.intermediary_in_scan,
            link_ab_counted_shared_kills: value.link_ab_counted_shared_kills,
            link_ab_most_recent_in_window_kill_time_utc: value.link_ab_most_recent_in_window_kill_time_utc,
            link_ab_split_bonus_applied: value.link_ab_split_bonus_applied,
            link_ab_strength: value.link_ab_strength,
            link_ab_confidence: value.link_ab_confidence,
            link_cb_counted_shared_kills: value.link_cb_counted_shared_kills,
            link_cb_most_recent_in_window_kill_time_utc: value.link_cb_most_recent_in_window_kill_time_utc,
            link_cb_split_bonus_applied: value.link_cb_split_bonus_applied,
            link_cb_strength: value.link_cb_strength,
            link_cb_confidence: value.link_cb_confidence,
            chain_age_days: value.chain_age_days,
            chain_strength_for_this_intermediary: value.chain_strength_for_this_intermediary,
            is_strongest_intermediary_for_pair: value.is_strongest_intermediary_for_pair,
            pair_distinct_intermediary_count: value.pair_distinct_intermediary_count,
            pair_intermediary_bonus: value.pair_intermediary_bonus,
            pair_chain_confidence: value.pair_chain_confidence,
        }
    }
}

impl From<IntermediaryHubSummary> for IntermediaryHubSummaryResponse {
    fn from(value: IntermediaryHubSummary) -> Self {
        Self {
            intermediary_pilot_character_id: value.intermediary_pilot_id,
            intermediary_in_scan: value.intermediary_in_scan,
            scanned_pilots_linked_count: value.scanned_pilots_linked_count,
        }
    }
}
