namespace Killright.Core.Models;

public sealed record EsiResolvedIdentity
{
    public required long Id { get; init; }
    public required string Name { get; init; }
}
