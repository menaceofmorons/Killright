using Killright.Core.Style;
using Killright.UI.Resources;
using Killright.UI.Style;
using Xunit;

namespace Killright.UI.Tests.Style;

public sealed class StyleCodesTests
{
    [Theory]
    [InlineData(StyleClassification.Unknown, "unknown", "Style.Word.Unknown")]
    [InlineData(StyleClassification.Inactive, "inactive", "Style.Word.Inactive")]
    [InlineData(StyleClassification.Victim, "victim", "Style.Word.Victim")]
    [InlineData(StyleClassification.SoloBeginner, "solo_beginner", "Style.Word.SoloBeginner")]
    [InlineData(StyleClassification.Solo, "solo", "Style.Word.Solo")]
    [InlineData(StyleClassification.GangBeginner, "gang_beginner", "Style.Word.GangBeginner")]
    [InlineData(StyleClassification.Gang, "gang", "Style.Word.Gang")]
    [InlineData(StyleClassification.Blob, "blob", "Style.Word.Blob")]
    [InlineData(StyleClassification.Fleet, "fleet", "Style.Word.Fleet")]
    public void Code_RoundTripsAndMapsToTheDisplayText(StyleClassification style, string code, string resourceKey)
    {
        Assert.Equal(code, StyleCodes.ToCode(style));
        Assert.True(StyleCodes.TryFromCode(code, out var parsed));
        Assert.Equal(style, parsed);
        Assert.Equal(Strings.Get(resourceKey), StyleDisplayFormatter.Format(parsed));
    }

    [Theory]
    [InlineData("Solo")]
    [InlineData("Vict")]
    [InlineData("Solo(b)")]
    [InlineData("")]
    [InlineData(null)]
    public void TryFromCode_DisplayTextOrEmpty_IsRejected(string? value)
    {
        Assert.False(StyleCodes.TryFromCode(value, out var style));
        Assert.Equal(StyleClassification.Unknown, style);
    }

    [Fact]
    public void GeneralStyleClassifier_Result_IsPassedAsACodeNotDisplayText()
    {
        var statistics = new Killright.Shared.zKill.zKillStatistics { shipsDestroyed = 10, soloKills = 3, soloRatio = 80, shipsLost = 1 };

        var code = StyleCodes.ToCode(GeneralStyleClassifier.Classify(statistics).Classification);

        Assert.Equal("solo_beginner", code);
        Assert.NotEqual(StyleDisplayFormatter.Format(StyleClassification.SoloBeginner), code);
    }
}
