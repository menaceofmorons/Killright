using Killright.Shared;

namespace Killright.Core.Models;

public sealed record Pilot
{
    public required string InputName { get; init; }
    public long? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public VerifyStatus VerifyStatus { get; init; }
    public double? SecurityStatus { get; init; }
    public Corporation? Corporation { get; init; }
    public Alliance? Alliance { get; init; }
}
