using System.Text.Json.Serialization;

namespace Killright.Shared.zKill;

public sealed class zKillStatisticsGroupBreakdown
{
    [JsonPropertyName("shipsDestroyed")]
    public int shipsDestroyed { get; set; }
}
