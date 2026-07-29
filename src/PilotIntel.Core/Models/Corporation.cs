namespace PilotIntel.Core.Models;

public sealed record Corporation
{
    public required long CorporationId { get; init; }
    public required string Name { get; init; }
    public string? Ticker { get; init; }
}
