using Killright.Core.Style;
using Killright.UI.Style;
using Xunit;

namespace Killright.UI.Tests.Style;

public sealed class StyleLetterCodeFormatterTests
{
    [Theory]
    [InlineData(StyleClassification.Unknown, "U")]
    [InlineData(StyleClassification.Inactive, "I")]
    [InlineData(StyleClassification.Victim, "V")]
    [InlineData(StyleClassification.SoloBeginner, "Sb")]
    [InlineData(StyleClassification.Solo, "S")]
    [InlineData(StyleClassification.GangBeginner, "Gb")]
    [InlineData(StyleClassification.Gang, "G")]
    [InlineData(StyleClassification.Blob, "B")]
    [InlineData(StyleClassification.Fleet, "F")]
    public void FormatGeneral_KnownValue_ReturnsLetterCode(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatGeneral(style, isPodder: false));
    }

    [Theory]
    [InlineData(StyleClassification.Miner, "U")]
    public void FormatGeneral_RecentOnlyValue_FallsBackToUnknown(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatGeneral(style, isPodder: false));
    }

    [Theory]
    [InlineData(StyleClassification.Unknown, "U")]
    [InlineData(StyleClassification.Inactive, "I")]
    [InlineData(StyleClassification.Victim, "V")]
    [InlineData(StyleClassification.Solo, "S")]
    [InlineData(StyleClassification.Gang, "G")]
    [InlineData(StyleClassification.Blob, "B")]
    [InlineData(StyleClassification.Fleet, "F")]
    [InlineData(StyleClassification.Miner, "M")]
    [InlineData(StyleClassification.Explorer, "E")]
    [InlineData(StyleClassification.Hauler, "H")]
    [InlineData(StyleClassification.PI, "P")]
    public void FormatRecent_KnownValue_ReturnsLetterCode(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatRecent(style, isPodder: false));
    }

    [Theory]
    [InlineData(StyleClassification.SoloBeginner, "U")]
    [InlineData(StyleClassification.GangBeginner, "U")]
    public void FormatRecent_GeneralOnlyBeginnerValue_FallsBackToUnknown(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatRecent(style, isPodder: false));
    }

    [Theory]
    [InlineData(StyleClassification.SoloBeginner, "Sbx")]
    [InlineData(StyleClassification.Solo, "Sx")]
    [InlineData(StyleClassification.GangBeginner, "Gbx")]
    [InlineData(StyleClassification.Gang, "Gx")]
    [InlineData(StyleClassification.Blob, "Bx")]
    public void FormatGeneral_IsPodderOnSupportedStyle_AppendsXSuffixAfterAnyBeginnerSuffix(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatGeneral(style, isPodder: true));
    }

    [Theory]
    [InlineData(StyleClassification.Victim)]
    [InlineData(StyleClassification.Fleet)]
    [InlineData(StyleClassification.Unknown)]
    public void FormatGeneral_IsPodderOnUnsupportedStyle_NoSuffixAppended(StyleClassification style)
    {
        var withoutSuffix = StyleLetterCodeFormatter.FormatGeneral(style, isPodder: false);

        Assert.Equal(withoutSuffix, StyleLetterCodeFormatter.FormatGeneral(style, isPodder: true));
    }

    [Theory]
    [InlineData(StyleClassification.Solo, "Sx")]
    [InlineData(StyleClassification.Gang, "Gx")]
    [InlineData(StyleClassification.Blob, "Bx")]
    public void FormatRecent_IsPodderOnSupportedStyle_AppendsXSuffix(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatRecent(style, isPodder: true));
    }

    [Fact]
    public void FormatRecent_IsPodderOnFleetStyle_NoSuffixAppended()
    {
        Assert.Equal("F", StyleLetterCodeFormatter.FormatRecent(StyleClassification.Fleet, isPodder: true));
    }
}
