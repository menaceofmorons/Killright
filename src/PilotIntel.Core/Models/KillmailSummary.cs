namespace PilotIntel.Core.Models;

public sealed record KillmailSummary
{
    public required long KillmailId { get; init; }
    public required DateTimeOffset KillTime { get; init; }
    public long? VictimCharacterId { get; init; }
    public required int AttackerCount { get; init; }
    public int? SolarSystemId { get; init; }
    public double? SystemSecurity { get; init; }
    public bool IsGateOrHighSec { get; init; }
    public IReadOnlyList<long> AttackerCharacterIds { get; init; } = Array.Empty<long>();
}
