using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class RelationshipConfidenceBandSettingTests
{
    [Fact]
    public void ValidateOrDefault_NullBands_ReturnsDefaults()
    {
        var log = new List<string>();

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(null, log.Add);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
        Assert.Equal(new[] { "Setting 'relationshipConfidenceBands' read as null; using the built-in bands (null)." }, log);
    }

    [Fact]
    public void ValidateOrDefault_EmptyBands_ReturnsDefaultsAndLogs()
    {
        var log = new List<string>();

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(Array.Empty<RelationshipConfidenceBandSetting>(), log.Add);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
        Assert.Equal(new[] { "Setting 'relationshipConfidenceBands' read as []; using the built-in bands (empty)." }, log);
    }

    [Fact]
    public void ValidateOrDefault_ContiguousCustomBands_ReturnsOrderedCustomBands()
    {
        var custom = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "High", MinimumScore = 51, MaximumScore = 100 },
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 50 }
        };

        var log = new List<string>();

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(custom, log.Add);

        Assert.Empty(log);
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

        var log = new List<string>();

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(malformed, log.Add);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
        Assert.Equal(new[] { "Setting 'relationshipConfidenceBands' read as [Low 1-20, High 30-100]; using the built-in bands (gapped, overlapping or not starting at 1)." }, log);
    }

    [Fact]
    public void ValidateOrDefault_OverlappingBands_FallsBackToDefaultsAndLogs()
    {
        var log = new List<string>();
        var malformed = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 60 },
            new RelationshipConfidenceBandSetting { Name = "High", MinimumScore = 50, MaximumScore = 100 }
        };

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(malformed, log.Add);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
        Assert.Single(log);
    }

    [Fact]
    public void ValidateOrDefault_DoesNotCoverFullRange_FallsBackToDefaults()
    {
        var malformed = new[]
        {
            new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 50 }
        };

        var log = new List<string>();

        var result = RelationshipConfidenceBandSetting.ValidateOrDefault(malformed, log.Add);

        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result);
        Assert.Equal(new[] { "Setting 'relationshipConfidenceBands' read as [Low 1-50]; using the built-in bands (not covering 1 to 100)." }, log);
    }
}
