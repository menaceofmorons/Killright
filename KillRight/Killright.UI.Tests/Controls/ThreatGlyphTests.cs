using System.Windows;
using System.Windows.Shapes;
using Killright.UI.Controls;
using Xunit;

namespace Killright.UI.Tests.Controls;

public sealed class ThreatGlyphTests
{
    [Theory]
    [InlineData("Low", ThreatGlyph.WedgeKind.None, null, "Brush.Threat.Amber", true)]
    [InlineData("Medium", ThreatGlyph.WedgeKind.Quarter, "Brush.Threat.Amber", "Brush.Border", true)]
    [InlineData("High", ThreatGlyph.WedgeKind.Half, "Brush.Threat.Amber", "Brush.Border", true)]
    [InlineData("Very High", ThreatGlyph.WedgeKind.ThreeQuarter, "Brush.Threat.Amber", "Brush.Border", true)]
    [InlineData("Extreme", ThreatGlyph.WedgeKind.Full, "Brush.Threat.Red", "Brush.Border", false)]
    [InlineData("None", ThreatGlyph.WedgeKind.None, null, "Brush.Border", false)]
    [InlineData("Unk", ThreatGlyph.WedgeKind.None, null, "Brush.Border", false)]
    public void Describe_BandMapsToWedgeFillAndRing(string band, ThreatGlyph.WedgeKind wedge, string? wedgeBrush, string ringBrush, bool showRing)
    {
        var spec = ThreatGlyph.Describe(band);

        Assert.Equal(wedge, spec.Wedge);
        Assert.Equal(wedgeBrush, spec.WedgeBrushKey);
        Assert.Equal(ringBrush, spec.RingBrushKey);
        Assert.Equal(showRing, spec.ShowRing);
    }

    [Fact]
    public void Describe_NoBandUsesRedRingOrYellow()
    {
        foreach (var band in new[] { "None", "Low", "Medium", "High", "Very High", "Extreme", "Unk" })
        {
            var spec = ThreatGlyph.Describe(band);

            Assert.NotEqual("Brush.Threat.Yellow", spec.WedgeBrushKey);
            Assert.NotEqual("Brush.Threat.Red", spec.RingBrushKey);
        }
    }

    [Theory]
    [InlineData("Low", false, true, false)]
    [InlineData("Very High", true, true, false)]
    [InlineData("Extreme", false, false, true)]
    public void Render_SetsWedgeRingAndFullFillVisibility(string band, bool wedgeVisible, bool ringVisible, bool fullVisible)
    {
        Sta.Run(() =>
        {
            var glyph = new ThreatGlyph { Band = band };

            Assert.Equal(wedgeVisible, ((System.Windows.Shapes.Path)glyph.FindName("FillWedge")).Visibility == Visibility.Visible);
            Assert.Equal(ringVisible, ((Ellipse)glyph.FindName("RingOutline")).Visibility == Visibility.Visible);
            Assert.Equal(fullVisible, ((Ellipse)glyph.FindName("FullFill")).Visibility == Visibility.Visible);
            Assert.Null(glyph.FindName("DangerRing"));
        });
    }
}
