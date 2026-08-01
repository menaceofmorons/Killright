using System.Text.Json.Serialization;

namespace Killright.Integration.Esi;

internal sealed record EsiUniverseIdsResponse
{
    [JsonPropertyName("characters")]
    public List<EsiResolvedEntity>? Characters { get; init; }
}

internal sealed record EsiResolvedEntity
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

internal sealed record EsiCharacterResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("corporation_id")]
    public long CorporationId { get; init; }

    [JsonPropertyName("alliance_id")]
    public long? AllianceId { get; init; }

    [JsonPropertyName("security_status")]
    public double? SecurityStatus { get; init; }
}

internal sealed record EsiCorporationResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string? Ticker { get; init; }
}

internal sealed record EsiAllianceResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string? Ticker { get; init; }
}
