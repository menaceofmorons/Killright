using System.Text.Json.Serialization;

namespace Killright.Shared.zKill;

public sealed class zKillStatisticsMonth
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("shipsLost")]
    public int ShipsLost { get; set; }

    [JsonPropertyName("shipsDestroyed")]
    public int ShipsDestroyed { get; set; }
}
