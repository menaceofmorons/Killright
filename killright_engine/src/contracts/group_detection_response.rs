use serde::Serialize;

use crate::group_analysis::group_relationship_scoring::{RelationshipLinkType, ScoredRelationship};

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct GroupDetectionResponse {
    pub relationships: Vec<GroupRelationshipResponse>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct GroupRelationshipResponse {
    pub pilot_a_character_id: i64,
    pub pilot_b_character_id: i64,
    pub link_type: String,
    pub strength: i32,
    pub confidence: i32,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub total_shared_kills: Option<i64>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub last_shared_kill_time_utc: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub intermediaries_in_scan: Option<Vec<i64>>,
}

impl From<ScoredRelationship> for GroupRelationshipResponse {
    fn from(relationship: ScoredRelationship) -> Self {
        Self {
            pilot_a_character_id: relationship.pilot_a,
            pilot_b_character_id: relationship.pilot_b,
            link_type: match relationship.link_type {
                RelationshipLinkType::Direct => "Direct".to_string(),
                RelationshipLinkType::Chain => "Chain".to_string(),
            },
            strength: relationship.strength,
            confidence: relationship.confidence,
            total_shared_kills: relationship.total_shared_kills,
            last_shared_kill_time_utc: relationship.last_shared_kill_time_utc,
            intermediaries_in_scan: relationship.intermediaries_in_scan,
        }
    }
}
