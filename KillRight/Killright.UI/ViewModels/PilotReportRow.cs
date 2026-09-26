using Killright.UI.Analysis;

namespace Killright.UI.ViewModels;

public class PilotReportRow
{
    public long? CharacterId { get; set; }
    public IReadOnlyList<PilotRelationship> GroupRelationships { get; set; } = Array.Empty<PilotRelationship>();
    public string Pilot { get; set; } = string.Empty;
    public bool EngineAnalysisFailed { get; set; }
    public string Verify { get; set; } = string.Empty;
    public string Threat { get; set; } = string.Empty;
    public string SecurityStatus { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public long? CorporationId { get; set; }
    public string Corporation { get; set; } = string.Empty;
    public long? AllianceId { get; set; }
    public string Alliance { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string GeneralStyle { get; set; } = string.Empty;
    public string RecentStyle { get; set; } = string.Empty;
    public string Week { get; set; } = string.Empty;
    public string Kills { get; set; } = string.Empty;
    public string Solos { get; set; } = string.Empty;
    public string LastKill { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Birthday { get; set; } = string.Empty;
    public string? StatsFailureSource { get; set; }
    public PilotLastActivitySummary? LastActivity { get; set; }
}