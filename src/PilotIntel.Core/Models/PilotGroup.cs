namespace PilotIntel.Core.Models;

public sealed record PilotGroup
{
    public required string GroupName { get; init; }
    public required int MemberCount { get; init; }
    public required IReadOnlyList<long> MemberCharacterIds { get; init; }
    public double CohesionScore { get; init; }
    public int CombinedKillsWeek { get; init; }
    public int CombinedSoloWeek { get; init; }
}
