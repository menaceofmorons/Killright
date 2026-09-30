using Killright.Core.Style;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Core.Tests.Style;

public sealed class GeneralStyleClassifierTests
{
    public GeneralStyleClassifierTests()
    {
        GeneralStyleClassifier.Configure(
            blobMinimumAverageAttackers: 5,
            fleetMinimumAverageAttackers: 11,
            podderMinimumSharePercent: 35,
            podderMinimumKillCount: 5);
    }

    private static zKillStatistics Statistics(int shipsDestroyed, int shipsLost, double soloRatio, int soloKills, double avgGangSize, int soloLosses = 0, int podKills = 0)
    {
        return new zKillStatistics
        {
            shipsDestroyed = shipsDestroyed,
            shipsLost = shipsLost,
            soloRatio = soloRatio,
            soloKills = soloKills,
            avgGangSize = avgGangSize,
            soloLosses = soloLosses,
            podKills = podKills
        };
    }

    [Fact]
    public void Classify_AverageBelowBlobBoundary_ReturnsGang()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.9));

        Assert.Equal(StyleClassification.Gang, result.Classification);
    }

    [Fact]
    public void Classify_AverageAtBlobBoundary_ReturnsBlob()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 5.0));

        Assert.Equal(StyleClassification.Blob, result.Classification);
    }

    [Fact]
    public void Classify_AverageAtFleetBoundary_ReturnsFleet()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 11.0));

        Assert.Equal(StyleClassification.Fleet, result.Classification);
    }

    [Fact]
    public void Classify_ConfiguredBoundaries_OverrideDefaults()
    {
        GeneralStyleClassifier.Configure(
            blobMinimumAverageAttackers: 3,
            fleetMinimumAverageAttackers: 6,
            podderMinimumSharePercent: 35,
            podderMinimumKillCount: 5);

        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0));

        Assert.Equal(StyleClassification.Blob, result.Classification);

        GeneralStyleClassifier.Configure(
            blobMinimumAverageAttackers: 5,
            fleetMinimumAverageAttackers: 11,
            podderMinimumSharePercent: 35,
            podderMinimumKillCount: 5);
    }

    [Fact]
    public void Classify_PodKills_ExcludedFromShipsDestroyedForClassificationThresholds()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 55, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0, podKills: 10));

        Assert.Equal(StyleClassification.GangBeginner, result.Classification);
    }

    [Fact]
    public void Classify_PodShareAndCountMeetMinimums_SetsPodderMarker()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 70, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0, podKills: 20));

        Assert.True(result.IsPodder);
    }

    [Fact]
    public void Classify_PodShareAtThirtyFivePercent_SetsPodderMarker()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 27, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0, podKills: 7));

        Assert.True(result.IsPodder);
    }

    [Fact]
    public void Classify_PodShareAtThirtyFourPercent_DoesNotSetPodderMarker()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 134, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0, podKills: 34));

        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_PodCountBelowMinimum_DoesNotSetPodderMarker()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0, podKills: 4));

        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_FleetStyle_NeverSetsPodderMarker()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 11.0, podKills: 50));

        Assert.Equal(StyleClassification.Fleet, result.Classification);
        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_ZeroKillsAndZeroLosses_ReturnsInactiveAndNotPodder()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 0, shipsLost: 0, soloRatio: 0, soloKills: 0, avgGangSize: 0));

        Assert.Equal(StyleClassification.Inactive, result.Classification);
        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_NoHistoryMarker_ReturnsInactive()
    {
        var result = GeneralStyleClassifier.Classify(new zKillStatistics { NoHistory = true });

        Assert.Equal(StyleClassification.Inactive, result.Classification);
        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_ZeroKillsWithOneLoss_ReturnsVictim()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 0, shipsLost: 1, soloRatio: 0, soloKills: 0, avgGangSize: 0));

        Assert.Equal(StyleClassification.Victim, result.Classification);
    }

    [Fact]
    public void Classify_OnlyPodKillsAndNoLosses_ReturnsInactiveAndNotPodder()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 8, shipsLost: 0, soloRatio: 0, soloKills: 0, avgGangSize: 0, podKills: 8));

        Assert.Equal(StyleClassification.Inactive, result.Classification);
        Assert.False(result.IsPodder);
    }

    [Fact]
    public void Classify_OnlyPodKillsWithLosses_ReturnsVictim()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 8, shipsLost: 2, soloRatio: 0, soloKills: 0, avgGangSize: 0, podKills: 8));

        Assert.Equal(StyleClassification.Victim, result.Classification);
    }

    [Fact]
    public void Classify_OneShipKillAndNoLosses_IsNotInactive()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 1, shipsLost: 0, soloRatio: 100, soloKills: 1, avgGangSize: 1));

        Assert.Equal(StyleClassification.SoloBeginner, result.Classification);
    }

    [Fact]
    public void Classify_NullStatistics_ReturnsUnknownAndNotPodder()
    {
        var result = GeneralStyleClassifier.Classify(null);

        Assert.Equal(StyleClassification.Unknown, result.Classification);
        Assert.False(result.IsPodder);
    }
}
