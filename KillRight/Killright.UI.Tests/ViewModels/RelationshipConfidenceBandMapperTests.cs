using Killright.UI.Configuration;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class RelationshipConfidenceBandMapperTests
{
    [Theory]
    [InlineData(1, "Low")]
    [InlineData(25, "Low")]
    [InlineData(26, "Medium")]
    [InlineData(75, "Medium")]
    [InlineData(76, "High")]
    [InlineData(100, "High")]
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
