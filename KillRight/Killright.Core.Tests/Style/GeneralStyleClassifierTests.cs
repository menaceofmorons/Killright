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
            fleetMinimumAverageAttackers: 11);
    }

    private static zKillStatistics Statistics(int shipsDestroyed, int shipsLost, double soloRatio, int soloKills, double avgGangSize, int soloLosses = 0)
    {
        return new zKillStatistics
        {
            shipsDestroyed = shipsDestroyed,
            shipsLost = shipsLost,
            soloRatio = soloRatio,
            soloKills = soloKills,
            avgGangSize = avgGangSize,
            soloLosses = soloLosses
        };
    }

    [Fact]
    public void Classify_AverageBelowBlobBoundary_ReturnsGang()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.9));

        Assert.Equal(StyleClassification.Gang, result);
    }

    [Fact]
    public void Classify_AverageAtBlobBoundary_ReturnsBlob()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 5.0));

        Assert.Equal(StyleClassification.Blob, result);
    }

    [Fact]
    public void Classify_AverageAtFleetBoundary_ReturnsFleet()
    {
        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 11.0));

        Assert.Equal(StyleClassification.Fleet, result);
    }

    [Fact]
    public void Classify_ConfiguredBoundaries_OverrideDefaults()
    {
        GeneralStyleClassifier.Configure(
            blobMinimumAverageAttackers: 3,
            fleetMinimumAverageAttackers: 6);

        var result = GeneralStyleClassifier.Classify(Statistics(shipsDestroyed: 60, shipsLost: 10, soloRatio: 10, soloKills: 2, avgGangSize: 4.0));

        Assert.Equal(StyleClassification.Blob, result);

        GeneralStyleClassifier.Configure(
            blobMinimumAverageAttackers: 5,
            fleetMinimumAverageAttackers: 11);
    }
}
