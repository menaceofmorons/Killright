//using Killright.Integration.zKill;
using Killright.Shared.zKill;

namespace Killright.Core.Style;

public static class GeneralStyleClassifier
{
    private static double _blobMinimumAverageAttackers = 5;
    private static double _fleetMinimumAverageAttackers = 11;

    public static void Configure(double blobMinimumAverageAttackers, double fleetMinimumAverageAttackers)
    {
        _blobMinimumAverageAttackers = blobMinimumAverageAttackers;
        _fleetMinimumAverageAttackers = fleetMinimumAverageAttackers;
    }

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

        if (statistics.soloRatio < 60 && statistics.avgGangSize < _blobMinimumAverageAttackers)
        {
            return statistics.shipsDestroyed < 50
                ? StyleClassification.GangBeginner
                : StyleClassification.Gang;
        }

        if (statistics.avgGangSize >= _blobMinimumAverageAttackers && statistics.avgGangSize < _fleetMinimumAverageAttackers)
            return StyleClassification.Blob;

        if (statistics.avgGangSize >= _fleetMinimumAverageAttackers)
            return StyleClassification.Fleet;

        return StyleClassification.Unknown;
    }
}
