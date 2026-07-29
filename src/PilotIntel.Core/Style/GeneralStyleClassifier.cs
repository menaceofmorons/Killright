//using PilotIntel.Integration.zKill;
using PilotIntel.Shared.zKill;

namespace PilotIntel.Core.Style;

public static class GeneralStyleClassifier
{
    public static StyleClassification Classify(zKillStatistics? statistics)
    {
        if (statistics is null)
            return StyleClassification.Unknown;

        if (statistics.shipsDestroyed == 0 && statistics.shipsLost > 0)
            return StyleClassification.Victim;

        if (statistics.shipsDestroyed > 0 && statistics.shipsLost > 0)
        {
            var destroyedToLostRatio = statistics.shipsDestroyed / (double)statistics.shipsLost;
            var soloLossesToDestroyedRatio = statistics.soloLosses / (double)statistics.shipsDestroyed;

            if (destroyedToLostRatio <= 0.2 && soloLossesToDestroyedRatio >= 0.7)
                return StyleClassification.Victim;
        }

        if (statistics.soloRatio >= 60)
        {
            return statistics.soloKills < 25
                ? StyleClassification.SoloBeginner
                : StyleClassification.Solo;
        }

        if (statistics.soloRatio < 60 && statistics.avgGangSize < 5)
        {
            return statistics.shipsDestroyed < 50
                ? StyleClassification.GangBeginner
                : StyleClassification.Gang;
        }

        if (statistics.avgGangSize >= 5 && statistics.avgGangSize < 11)
            return StyleClassification.Blob;

        if (statistics.avgGangSize >= 11)
            return StyleClassification.Fleet;

        return StyleClassification.Unknown;
    }
}