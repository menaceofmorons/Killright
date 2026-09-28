using Killright.UI.Resources;
using Xunit;

namespace Killright.UI.Tests.Resources;

public sealed class UiTextTests
{
    [Fact]
    public void GridColumnHeaders_ResolveFromResx()
    {
        Assert.Equal("Pilot", UiText.GridColumnHeaderPilot);
        Assert.Equal("Alliance", UiText.GridColumnHeaderAlliance);
        Assert.Equal("Last Kill", UiText.GridColumnHeaderLastKill);
    }

    [Fact]
    public void FormatEsiUnresolved_SubstitutesPlaceholders()
    {
        Assert.Equal(
            "ESI could not resolve \"Some Pilot\" as a Pilot.",
            UiText.FormatEsiUnresolved("Some Pilot", "Pilot"));
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Very High")]
    public void GetThreatBandDisplay_ResolvesKnownBand(string band)
    {
        Assert.Equal(band, UiText.GetThreatBandDisplay(band));
    }

    [Fact]
    public void GetThreatBandDisplay_BlankBand_FallsBackToUnk()
    {
        Assert.Equal("Unk", UiText.GetThreatBandDisplay(string.Empty));
    }

    [Fact]
    public void GetThreatBandDisplay_UnrecognisedBand_FallsBackToRawValue()
    {
        Assert.Equal("SomeNewBand", UiText.GetThreatBandDisplay("SomeNewBand"));
    }
}
