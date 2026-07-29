use std::collections::HashMap;
use std::sync::OnceLock;

use crate::pintel_engine::ship_library::configuration::load_default_configuration;
use crate::pintel_engine::ship_library::models::{
    ShipClassification,
    ShipClassificationConfiguration,
};

static CLASSIFIER: OnceLock<Option<ShipClassifier>> = OnceLock::new();

pub struct ShipClassifier {
    lookup: HashMap<i64, ShipClassification>,
}

impl ShipClassifier {
    pub fn from_configuration(configuration: ShipClassificationConfiguration) -> Self {
        let mut lookup = HashMap::new();
        let mut classifications = configuration.classifications;

        classifications.sort_by_key(|classification| classification.priority);

        for classification in classifications {
            for ship in classification.ships {
                lookup.entry(ship.type_id).or_insert_with(|| ShipClassification {
                    name: classification.name.clone(),
                    recent_style_label: classification.recent_style_label.clone(),
                    priority: classification.priority,
                    ship_name: ship.name,
                });
            }
        }

        Self { lookup }
    }

    pub fn classify(&self, ship_type_id: Option<i64>) -> Option<&ShipClassification> {
        let ship_type_id = ship_type_id?;
        self.lookup.get(&ship_type_id)
    }
}

pub fn classify_ship(ship_type_id: Option<i64>) -> Option<String> {

    let classifier = CLASSIFIER.get_or_init(|| {

        match load_default_configuration() {

            Ok(configuration) => {

                eprintln!(
                    "Ship classification configuration loaded successfully");

                Some(
                    ShipClassifier::from_configuration(
                        configuration))
            }

            Err(error) => {

                eprintln!(
                    "Failed to load ship classifications: {}",
                    error);

                None
            }
        }
    });

    classifier
        .as_ref()?
        .classify(ship_type_id)
        .map(|classification|
            classification.recent_style_label.clone())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::pintel_engine::ship_library::models::{
        ShipClassificationDefinition,
        ShipDefinition,
        ShipClassificationConfiguration,
    };

    fn configuration() -> ShipClassificationConfiguration {
        ShipClassificationConfiguration {
            version: "test".to_string(),
            last_updated: "2026-07-29".to_string(),
            classifications: vec![
                ShipClassificationDefinition {
                    name: "PI".to_string(),
                    recent_style_label: "PI".to_string(),
                    priority: 10,
                    ships: vec![ShipDefinition {
                        type_id: 655,
                        name: "Epithal".to_string(),
                    }],
                },
                ShipClassificationDefinition {
                    name: "Hauler".to_string(),
                    recent_style_label: "Hauler".to_string(),
                    priority: 40,
                    ships: vec![ShipDefinition {
                        type_id: 655,
                        name: "Epithal".to_string(),
                    }],
                },
                ShipClassificationDefinition {
                    name: "Explorer".to_string(),
                    recent_style_label: "Explorer".to_string(),
                    priority: 30,
                    ships: vec![ShipDefinition {
                        type_id: 605,
                        name: "Heron".to_string(),
                    }],
                },
            ],
        }
    }

    #[test]
    fn lower_priority_number_wins_duplicate_ship_type_id() {
        let classifier = ShipClassifier::from_configuration(configuration());
        let result = classifier.classify(Some(655)).unwrap();

        assert_eq!(result.recent_style_label, "PI");
        assert_eq!(result.ship_name, "Epithal");
    }

    #[test]
    fn known_ship_returns_classification() {
        let classifier = ShipClassifier::from_configuration(configuration());
        let result = classifier.classify(Some(605)).unwrap();

        assert_eq!(result.name, "Explorer");
        assert_eq!(result.recent_style_label, "Explorer");
        assert_eq!(result.ship_name, "Heron");
    }

    #[test]
    fn unknown_ship_returns_none() {
        let classifier = ShipClassifier::from_configuration(configuration());
        assert!(classifier.classify(Some(999999)).is_none());
    }

    #[test]
    fn null_ship_returns_none() {
        let classifier = ShipClassifier::from_configuration(configuration());
        assert!(classifier.classify(None).is_none());
    }
}