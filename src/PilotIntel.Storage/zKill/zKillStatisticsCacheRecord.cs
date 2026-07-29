using PilotIntel.Shared.zKill;

namespace PilotIntel.Storage.zKill;

public sealed class zKillStatisticsCacheRecord
{
    public required long CharacterId { get; init; }
    public required int ShipsDestroyed { get; init; }
    public required int SoloKills { get; init; }
    public required double SoloRatio { get; init; }
    public required double AvgGangSize { get; init; }
    public required int ShipsLost { get; init; }
    public required int SoloLosses { get; init; }
    public required string GeneralStyle { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }

    public zKillStatistics ToStatistics()
    {
        return new zKillStatistics
        {
            shipsDestroyed = ShipsDestroyed,
            soloKills = SoloKills,
            soloRatio = SoloRatio,
            avgGangSize = AvgGangSize,
            shipsLost = ShipsLost,
            soloLosses = SoloLosses
        };
    }

    public static zKillStatisticsCacheRecord FromStatistics(
        long characterId,
        zKillStatistics statistics,
        string generalStyle,
        DateTimeOffset checkedAtUtc)
    {
        return new zKillStatisticsCacheRecord
        {
            CharacterId = characterId,
            ShipsDestroyed = statistics.shipsDestroyed,
            SoloKills = statistics.soloKills,
            SoloRatio = statistics.soloRatio,
            AvgGangSize = statistics.avgGangSize,
            ShipsLost = statistics.shipsLost,
            SoloLosses = statistics.soloLosses,
            GeneralStyle = generalStyle,
            CheckedAtUtc = checkedAtUtc
        };
    }
}