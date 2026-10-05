use serde::Serialize;

use crate::activity_analysis::DerivedActivity;

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct NewestKillmailResponse {
    pub kill_time_utc: String,
    pub activity_type: String,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct DerivedActivityResponse {
    pub has_public_activity_data: bool,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub kills_week: Option<i32>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub solo_week: Option<i32>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub info_week_losses: Option<i32>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub newest_non_pod_killmail: Option<NewestKillmailResponse>,

    #[serde(skip_serializing_if = "Option::is_none")]
    pub newest_non_pod_kill_time_utc: Option<String>,
}

impl From<DerivedActivity> for DerivedActivityResponse {
    fn from(activity: DerivedActivity) -> Self {
        let has_data = activity.has_public_activity_data;

        Self {
            has_public_activity_data: has_data,
            kills_week: has_data.then_some(activity.kills_week),
            solo_week: has_data.then_some(activity.solo_week),
            info_week_losses: has_data.then_some(activity.info_week_losses),
            newest_non_pod_killmail: activity.newest_non_pod_killmail.map(|killmail| NewestKillmailResponse {
                kill_time_utc: killmail.kill_time_utc,
                activity_type: if killmail.is_loss { "Loss".to_string() } else { "Kill".to_string() },
            }),
            newest_non_pod_kill_time_utc: activity.newest_non_pod_kill_time_utc,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::activity_analysis::NewestKillmail;

    #[test]
    fn without_public_activity_omits_counts_and_newest() {
        let response = DerivedActivityResponse::from(DerivedActivity {
            has_public_activity_data: false,
            kills_week: 0,
            solo_week: 0,
            info_week_losses: 0,
            newest_non_pod_killmail: None,
            newest_non_pod_kill_time_utc: None,
        });

        assert_eq!(serde_json::to_string(&response).unwrap(), "{\"has_public_activity_data\":false}");
    }

    #[test]
    fn with_public_activity_carries_counts_and_type() {
        let response = DerivedActivityResponse::from(DerivedActivity {
            has_public_activity_data: true,
            kills_week: 3,
            solo_week: 1,
            info_week_losses: 4,
            newest_non_pod_killmail: Some(NewestKillmail {
                kill_time_utc: "2026-09-28T00:00:00+00:00".to_string(),
                is_loss: true,
            }),
            newest_non_pod_kill_time_utc: Some("2026-09-27T00:00:00+00:00".to_string()),
        });
        let json = serde_json::to_string(&response).unwrap();

        assert!(json.contains("\"kills_week\":3"));
        assert!(json.contains("\"solo_week\":1"));
        assert!(json.contains("\"info_week_losses\":4"));
        assert!(json.contains("\"activity_type\":\"Loss\""));
        assert!(json.contains("\"newest_non_pod_kill_time_utc\":\"2026-09-27T00:00:00+00:00\""));
    }
}
