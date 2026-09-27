using System.Windows.Media;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class HighlightColorCalculatorTests
{
    private static readonly Color LightBlue = Color.FromRgb(0xAD, 0xD8, 0xE6);

    [Fact]
    public void ForPilot_AppliesOpacityAsAlphaOnly_RgbUnchanged()
    {
        var result = HighlightColorCalculator.ForPilot(LightBlue, 0.2);

        Assert.Equal((byte)51, result.A);
        Assert.Equal(LightBlue.R, result.R);
        Assert.Equal(LightBlue.G, result.G);
        Assert.Equal(LightBlue.B, result.B);
    }

    [Fact]
    public void ForRelated_AppliesOpacityAsAlphaOnly_RgbUnchanged()
    {
        var relatedColor = Color.FromRgb(0x90, 0xEE, 0x90);

        var result = HighlightColorCalculator.ForRelated(relatedColor, 0.5);

        Assert.Equal((byte)128, result.A);
        Assert.Equal(relatedColor.R, result.R);
        Assert.Equal(relatedColor.G, result.G);
        Assert.Equal(relatedColor.B, result.B);
    }

    [Fact]
    public void ForSameGroup_ProducesLighterShadeThanPilotColor()
    {
        var pilotOpaque = HighlightColorCalculator.ForPilot(LightBlue, 1.0);
        var sameGroupOpaque = HighlightColorCalculator.ForSameGroup(LightBlue, 1.0);

        var pilotLuminance = pilotOpaque.R + pilotOpaque.G + pilotOpaque.B;
        var sameGroupLuminance = sameGroupOpaque.R + sameGroupOpaque.G + sameGroupOpaque.B;

        Assert.True(sameGroupLuminance > pilotLuminance);
    }

    [Fact]
    public void ForSameGroup_SameOpacityAsPilotAndRelated()
    {
        var pilot = HighlightColorCalculator.ForPilot(LightBlue, 0.3);
        var sameGroup = HighlightColorCalculator.ForSameGroup(LightBlue, 0.3);

        Assert.Equal(pilot.A, sameGroup.A);
    }

    [Fact]
    public void ForPilot_OpacityOutOfRange_ClampedToValidByte()
    {
        var overOpacity = HighlightColorCalculator.ForPilot(LightBlue, 1.5);
        var underOpacity = HighlightColorCalculator.ForPilot(LightBlue, -0.5);

        Assert.Equal((byte)255, overOpacity.A);
        Assert.Equal((byte)0, underOpacity.A);
    }
}
