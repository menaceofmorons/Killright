using System.Text.Json.Serialization;

namespace PilotIntel.Shared.zKill;

public sealed class zKillStatistics
{
    [JsonPropertyName("shipsDestroyed")]
    public int shipsDestroyed { get; set; }

    [JsonPropertyName("soloKills")]
    public int soloKills { get; set; }

    [JsonPropertyName("soloRatio")]
    public double soloRatio { get; set; }

    [JsonPropertyName("avgGangSize")]
    public double avgGangSize { get; set; }

    [JsonPropertyName("shipsLost")]
    public int shipsLost { get; set; }

    [JsonPropertyName("soloLosses")]
    public int soloLosses { get; set; }
}