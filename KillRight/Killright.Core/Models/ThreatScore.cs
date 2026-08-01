using Killright.Shared;

namespace Killright.Core.Models;

public sealed record ThreatScore
{
    public required long CharacterId { get; init; }
    public required double Score { get; init; }
    public required ThreatLevel Level { get; init; }
    public double Confidence { get; init; }
}
