using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class RelationshipConfidenceBandSettingTests
{
    [Fact]
    public void ValidateOrDefault_NullBands_ReturnsDefaults()
    {
        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(null);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
    }

    [Fact]
    public void ValidateOrDefault_ContiguousCustomBands_ReturnsOrderedCustomBands()
    {
        var custom = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "High", MinimumScore = 51, MaximumScore = 100 },
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 50 }
        };

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(custom);

        Assert.Equal("Low", result[0].Name);
        Assert.Equal("High", result[1].Name);
    }

    [Fact]
    public void ValidateOrDefault_GapBetweenBands_FallsBackToDefaults()
    {
        var malformed = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 20 },
            new RelationshipConfidenceBandSetting { Name = "High", MinimumScore = 30, MaximumScore = 100 }
        };

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(malformed);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
    }

    [Fact]
    public void ValidateOrDefault_DoesNotCoverFullRange_FallsBackToDefaults()
    {
        var malformed = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 50 }
        };

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(malformed);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
    }
}
