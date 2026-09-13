/// Design_Spec_Dense.md §4.8, §6.11.4.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Confidence {
    Low,
    Average,
    High,
}

impl std::fmt::Display for Confidence {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let text = match self {
            Confidence::Low => "Low",
            Confidence::Average => "Average",
            Confidence::High => "High",
        };
        write!(formatter, "{text}")
    }
}

/// Design_Spec_Dense.md §6.11.4 Table53.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConfidencePattern {
    NeverSameCA,
    Recency,
    TwiceWithGap,
    ThreeOrMore,
}

pub const NEVER_SAME_CA_AVERAGE_MIN_KILLS: i64 = 5;
pub const NEVER_SAME_CA_HIGH_MIN_KILLS: i64 = 10;
pub const RECENCY_OR_GAP_AVERAGE_MIN_KILLS: i64 = 2;
pub const RECENCY_OR_GAP_HIGH_MIN_KILLS: i64 = 5;
pub const THREE_OR_MORE_HIGH_MIN_EPISODES: i64 = 4;

/// Design_Spec_Dense.md §6.11.4 Table53.
pub fn assign_confidence(pattern: ConfidencePattern, basis: i64) -> Confidence {
    match pattern {
        ConfidencePattern::NeverSameCA => {
            if basis >= NEVER_SAME_CA_HIGH_MIN_KILLS {
                Confidence::High
            } else if basis >= NEVER_SAME_CA_AVERAGE_MIN_KILLS {
                Confidence::Average
            } else {
                Confidence::Low
            }
        }
        ConfidencePattern::Recency | ConfidencePattern::TwiceWithGap => {
            if basis >= RECENCY_OR_GAP_HIGH_MIN_KILLS {
                Confidence::High
            } else if basis >= RECENCY_OR_GAP_AVERAGE_MIN_KILLS {
                Confidence::Average
            } else {
                Confidence::Low
            }
        }
        ConfidencePattern::ThreeOrMore => {
            if basis >= THREE_OR_MORE_HIGH_MIN_EPISODES {
                Confidence::High
            } else {
                Confidence::Average
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn assign_confidence_never_same_ca_bands() {
        assert_eq!(assign_confidence(ConfidencePattern::NeverSameCA, 4), Confidence::Low);
        assert_eq!(assign_confidence(ConfidencePattern::NeverSameCA, 5), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::NeverSameCA, 9), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::NeverSameCA, 10), Confidence::High);
    }

    #[test]
    fn assign_confidence_recency_bands() {
        assert_eq!(assign_confidence(ConfidencePattern::Recency, 1), Confidence::Low);
        assert_eq!(assign_confidence(ConfidencePattern::Recency, 2), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::Recency, 4), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::Recency, 5), Confidence::High);
    }

    #[test]
    fn assign_confidence_twice_with_gap_bands() {
        assert_eq!(assign_confidence(ConfidencePattern::TwiceWithGap, 1), Confidence::Low);
        assert_eq!(assign_confidence(ConfidencePattern::TwiceWithGap, 4), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::TwiceWithGap, 5), Confidence::High);
    }

    #[test]
    fn assign_confidence_three_or_more_bands() {
        assert_eq!(assign_confidence(ConfidencePattern::ThreeOrMore, 3), Confidence::Average);
        assert_eq!(assign_confidence(ConfidencePattern::ThreeOrMore, 4), Confidence::High);
        assert_eq!(assign_confidence(ConfidencePattern::ThreeOrMore, 9), Confidence::High);
    }
}
