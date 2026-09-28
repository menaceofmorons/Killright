pub mod threat_analysis_response;
pub mod threat_analyzer;
pub mod threat_configuration_loader;
pub mod threat_configuration_models;
pub mod threat_diagnostics;

#[cfg(test)]
pub(crate) mod threat_test_fixtures;

pub use threat_analysis_response::ThreatAnalysisResponse;
pub use threat_analyzer::analyze_intrinsic_threat;
pub use threat_configuration_models::ThreatConfiguration;
pub use threat_diagnostics::analyze_intrinsic_threat_diagnostics;
