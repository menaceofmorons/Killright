using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Killright.UI.Resources;

namespace Killright.UI.Controls;

public partial class ThreatGlyph : UserControl
{
    public static readonly DependencyProperty BandProperty = DependencyProperty.Register(
        nameof(Band),
        typeof(string),
        typeof(ThreatGlyph),
        new PropertyMetadata(string.Empty, OnBandChanged));

    private static readonly Geometry QuarterWedge = Geometry.Parse("M8,8 L8,0 A8,8 0 0 1 16,8 Z");
    private static readonly Geometry HalfWedge = Geometry.Parse("M8,8 L8,0 A8,8 0 0 1 8,16 Z");
    private static readonly Geometry ThreeQuarterWedge = Geometry.Parse("M8,8 L8,0 A8,8 0 1 1 0,8 Z");

    public ThreatGlyph()
    {
        InitializeComponent();
        Render(Band);
    }

    public string Band
    {
        get => (string)GetValue(BandProperty);
        set => SetValue(BandProperty, value);
    }

    private static void OnBandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ThreatGlyph)d).Render((string)e.NewValue);
    }

    public enum WedgeKind
    {
        None,
        Quarter,
        Half,
        ThreeQuarter,
        Full
    }

    public sealed record GlyphSpec(WedgeKind Wedge, string? WedgeBrushKey, string RingBrushKey, bool ShowRing, string Text);

    public static GlyphSpec Describe(string band) => band switch
    {
        "None" => new GlyphSpec(WedgeKind.None, null, "Brush.Border", false, UiText.PlaceholderDash),
        "Low" => new GlyphSpec(WedgeKind.None, null, "Brush.Threat.Amber", true, string.Empty),
        "Medium" => new GlyphSpec(WedgeKind.Quarter, "Brush.Threat.Amber", "Brush.Border", true, string.Empty),
        "High" => new GlyphSpec(WedgeKind.Half, "Brush.Threat.Amber", "Brush.Border", true, string.Empty),
        "Very High" => new GlyphSpec(WedgeKind.ThreeQuarter, "Brush.Threat.Amber", "Brush.Border", true, string.Empty),
        "Extreme" => new GlyphSpec(WedgeKind.Full, "Brush.Threat.Red", "Brush.Border", false, string.Empty),
        _ => new GlyphSpec(WedgeKind.None, null, "Brush.Border", false, "?")
    };

    private void Render(string band)
    {
        var spec = Describe(band);

        TextGlyph.Text = spec.Text;
        RingOutline.Visibility = spec.ShowRing ? Visibility.Visible : Visibility.Collapsed;
        RingOutline.SetResourceReference(Shape.StrokeProperty, spec.RingBrushKey);
        FullFill.Visibility = spec.Wedge == WedgeKind.Full ? Visibility.Visible : Visibility.Collapsed;

        var geometry = spec.Wedge switch
        {
            WedgeKind.Quarter => QuarterWedge,
            WedgeKind.Half => HalfWedge,
            WedgeKind.ThreeQuarter => ThreeQuarterWedge,
            _ => null
        };

        if (geometry is null || spec.WedgeBrushKey is null)
        {
            FillWedge.Visibility = Visibility.Collapsed;
        }
        else
        {
            FillWedge.Data = geometry;
            FillWedge.SetResourceReference(Shape.FillProperty, spec.WedgeBrushKey);
            FillWedge.Visibility = Visibility.Visible;
        }

        ToolTip = UiText.GetThreatBandDisplay(band);
    }
}
