namespace Killright.Shared;

public enum VerifyStatus
{
    NoMatch = 0,
    Partial = 1,
    Verified = 2
}

public enum ThreatLevel
{
    Unknown = 0,
    Dormant = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    VeryHigh = 5,
    Extreme = 6
}

public enum CombatStyle
{
    Unknown = 0,
    NoActivity = 1,
    Solo = 2,
    SmallGang = 3,
    Blob = 4,
    Fleet = 5
}

public enum AnalysisMode
{
    Quick = 0,
    Standard = 1,
    Deep = 2,
    Forensic = 3
}
