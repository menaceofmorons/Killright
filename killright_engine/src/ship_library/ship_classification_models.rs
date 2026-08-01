use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct ShipClassificationConfiguration {
    pub version: String,
    pub last_updated: String,
    pub classifications: Vec<ShipClassificationDefinition>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ShipClassificationDefinition {
    pub name: String,
    pub recent_style_label: String,
    pub priority: i32,
    pub ships: Vec<ShipDefinition>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ShipDefinition {
    pub type_id: i64,
    pub name: String,
}
