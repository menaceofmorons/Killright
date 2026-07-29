namespace PilotIntel.Core.Models;

public sealed record Alliance
{
    public required long AllianceId { get; init; }
    public required string Name { get; init; }
    public string? Ticker { get; init; }
}
