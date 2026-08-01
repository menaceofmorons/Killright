use std::collections::HashMap;
use std::sync::OnceLock;

use crate::pintel_engine::ship_library::configuration_loader::load_default_configuration;
use crate::pintel_engine::ship_library::ship_classification_models::{
    ShipClassificationConfiguration,
    ShipClassificationDefinition,
};

static CLASSIFIER: OnceLock<Option<ShipClassifier>> = OnceLock::new();

pub struct ShipClassifier {
    lookup: HashMap<i64, ShipClassificationDefinition>,
}

impl ShipClassifier {
    pub fn from_configuration(
        configuration: ShipClassificationConfiguration)
        -> Self
    {
        let mut lookup = HashMap::new();
        let mut classifications = configuration.classifications;

        classifications.sort_by_key(|classification| classification.priority);

        for classification in classifications {
            for ship in &classification.ships {
                lookup
                    .entry(ship.type_id)
                    .or_insert_with(|| classification.clone());
            }
        }

        Self {
            lookup,
        }
    }

    pub fn classify(
        &self,
        ship_type_id: Option<i64>)
        -> Option<&ShipClassificationDefinition>
    {
        let ship_type_id = ship_type_id?;

        self.lookup.get(&ship_type_id)
    }
}

pub fn classify_ship(
    ship_type_id: Option<i64>)
    -> Option<String>
{
    let classifier = CLASSIFIER.get_or_init(|| {
        load_default_configuration()
            .map(ShipClassifier::from_configuration)
            .ok()
    });

    classifier
        .as_ref()?
        .classify(ship_type_id)
        .map(|classification| classification.name.clone())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::pintel_engine::shared::recent_style_contract::*;
    use crate::pintel_engine::ship_library::ship_classification_models::ShipDefinition;

    fn test_configuration()
        -> ShipClassificationConfiguration
    {
        ShipClassificationConfiguration {
            version: "test".to_string(),
            last_updated: "2026-07-29".to_string(),
            classifications: vec![
                ShipClassificationDefinition {
                    name: STYLE_PI.to_string(),
                    recent_style_label: "PI".to_string(),
                    priority: 10,
                    ships: vec![ShipDefinition {
                        type_id: 655,
                        name: "Epithal".to_string(),
                    }],
                },
                ShipClassificationDefinition {
                    name: STYLE_EXPLORER.to_string(),
                    recent_style_label: "Explo".to_string(),
                    priority: 20,
                    ships: vec![ShipDefinition {
                        type_id: 605,
                        name: "Heron".to_string(),
                    }],
                },
            ],
        }
    }

    #[test]
    fn known_ship_returns_contract_classification() {
        let classifier = ShipClassifier::from_configuration(test_configuration());
        let classification = classifier.classify(Some(605));

        assert_eq!(classification.unwrap().name, STYLE_EXPLORER);
    }

    #[test]
    fn configured_display_label_does_not_drive_contract_value() {
        let classifier = ShipClassifier::from_configuration(test_configuration());
        let classification = classifier.classify(Some(605));

        assert_eq!(classification.unwrap().name, STYLE_EXPLORER);
        assert_eq!(classification.unwrap().recent_style_label, "Explo");
    }

    #[test]
    fn unknown_ship_returns_none() {
        let classifier = ShipClassifier::from_configuration(test_configuration());
        let classification = classifier.classify(Some(999999));

        assert!(classification.is_none());
    }

    #[test]
    fn null_ship_returns_none() {
        let classifier = ShipClassifier::from_configuration(test_configuration());
        let classification = classifier.classify(None);

        assert!(classification.is_none());
    }
}