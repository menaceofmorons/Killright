using Killright.UI.Configuration;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class RelationshipConfidenceBandMapperTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(25, "1")]
    [InlineData(26, "2")]
    [InlineData(75, "2")]
    [InlineData(76, "3")]
    [InlineData(100, "3")]
    public void MapScore_WithinDefaultBands_ReturnsExpectedName(int confidence, string expected)
    {
        var result = RelationshipConfidenceBandMapper.MapScore(confidence, RelationshipConfidenceBandSetting.Defaults);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MapScore_OutsideAllBands_ReturnsEmpty()
    {
        var result = RelationshipConfidenceBandMapper.MapScore(0, RelationshipConfidenceBandSetting.Defaults);

        Assert.Equal(string.Empty, result);
    }
}
