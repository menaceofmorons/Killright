using Killright.Core.Style;
using Xunit;

namespace Killright.Core.Tests.Style;

public sealed class StyleLetterCodeFormatterTests
{
    [Theory]
    [InlineData(StyleClassification.Unknown, "U")]
    [InlineData(StyleClassification.Victim, "V")]
    [InlineData(StyleClassification.SoloBeginner, "Sb")]
    [InlineData(StyleClassification.Solo, "S")]
    [InlineData(StyleClassification.GangBeginner, "Gb")]
    [InlineData(StyleClassification.Gang, "G")]
    [InlineData(StyleClassification.Blob, "B")]
    [InlineData(StyleClassification.Fleet, "F")]
    public void FormatGeneral_KnownValue_ReturnsLetterCode(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatGeneral(style));
    }

    [Theory]
    [InlineData(StyleClassification.Inactive, "U")]
    [InlineData(StyleClassification.Miner, "U")]
    public void FormatGeneral_RecentOnlyValue_FallsBackToUnknown(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatGeneral(style));
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
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatRecent(style));
    }

    [Theory]
    [InlineData(StyleClassification.SoloBeginner, "U")]
    [InlineData(StyleClassification.GangBeginner, "U")]
    public void FormatRecent_GeneralOnlyBeginnerValue_FallsBackToUnknown(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleLetterCodeFormatter.FormatRecent(style));
    }
}
