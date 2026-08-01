using Killright.Shared;

namespace Killright.Shared.Contracts;

public sealed record KillRightRow(
    string Pilot,
    VerifyStatus Verify,
    ThreatLevel Threat,
    double? SecurityStatus,
    string Group,
    string Corporation,
    string Alliance,
    CombatStyle RecentStyle,
    CombatStyle OverallStyle,
    int? KillsWeek,
    int? SoloWeek,
    string Notes);
