//using Killright.Integration.zKill;
using Killright.Shared.zKill;

namespace Killright.Core.Style;

public static class GeneralStyleClassifier
{
    private static double _blobMinimumAverageAttackers = 5;
    private static double _fleetMinimumAverageAttackers = 11;
    private static double _podderMinimumSharePercent = 35;
    private static int _podderMinimumKillCount = 5;

    public static void Configure(
        double blobMinimumAverageAttackers,
        double fleetMinimumAverageAttackers,
        double podderMinimumSharePercent,
        int podderMinimumKillCount)
    {
        _blobMinimumAverageAttackers = blobMinimumAverageAttackers;
        _fleetMinimumAverageAttackers = fleetMinimumAverageAttackers;
        _podderMinimumSharePercent = podderMinimumSharePercent;
        _podderMinimumKillCount = podderMinimumKillCount;
    }

    public static GeneralStyleResult Classify(zKillStatistics? statistics)
    {
        if (statistics is null)
            return new GeneralStyleResult(StyleClassification.Unknown, false);

        var shipsDestroyed = Math.Max(0, statistics.shipsDestroyed - statistics.podKills);

        if (shipsDestroyed == 0 && statistics.shipsLost > 0)
            return new GeneralStyleResult(StyleClassification.Victim, false);

        if (shipsDestroyed > 0 && statistics.shipsLost > 0)
        {
            var destroyedToLostRatio = shipsDestroyed / (double)statistics.shipsLost;
            var soloLossesToDestroyedRatio = statistics.soloLosses / (double)shipsDestroyed;

            if (destroyedToLostRatio <= 0.2 && soloLossesToDestroyedRatio >= 0.7)
                return new GeneralStyleResult(StyleClassification.Victim, false);
        }

        StyleClassification classification;

        if (statistics.soloRatio >= 60)
        {
            classification = statistics.soloKills < 25
                ? StyleClassification.SoloBeginner
                : StyleClassification.Solo;
        }
        else if (statistics.avgGangSize < _blobMinimumAverageAttackers)
        {
            classification = shipsDestroyed < 50
                ? StyleClassification.GangBeginner
                : StyleClassification.Gang;
        }
        else if (statistics.avgGangSize < _fleetMinimumAverageAttackers)
        {
            classification = StyleClassification.Blob;
        }
        else
        {
            classification = StyleClassification.Fleet;
        }

        var isPodder = IsPodder(classification, statistics.podKills, shipsDestroyed);

        return new GeneralStyleResult(classification, isPodder);
    }

    private static bool IsPodder(StyleClassification classification, int podKills, int shipsDestroyedExcludingPods)
    {
        if (classification is not (StyleClassification.Solo or StyleClassification.SoloBeginner
            or StyleClassification.Gang or StyleClassification.GangBeginner or StyleClassification.Blob))
        {
            return false;
        }

        if (shipsDestroyedExcludingPods == 0 || podKills < _podderMinimumKillCount)
            return false;

        var sharePercent = podKills / (double)shipsDestroyedExcludingPods * 100;

        return sharePercent >= _podderMinimumSharePercent;
    }
}
