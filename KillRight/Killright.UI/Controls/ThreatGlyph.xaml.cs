using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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

    private void Render(string band)
    {
        TextGlyph.Text = string.Empty;
        FillWedge.Visibility = Visibility.Collapsed;
        FullFill.Visibility = Visibility.Collapsed;
        DangerRing.Visibility = Visibility.Collapsed;
        RingOutline.Visibility = Visibility.Visible;

        switch (band)
        {
            case "None":
                TextGlyph.Text = "-";
                RingOutline.Visibility = Visibility.Collapsed;
                break;
            case "Low":
                FillWedge.Data = QuarterWedge;
                FillWedge.SetResourceReference(Shape.FillProperty, "Brush.Threat.Yellow");
                FillWedge.Visibility = Visibility.Visible;
                break;
            case "Medium":
                FillWedge.Data = HalfWedge;
                FillWedge.SetResourceReference(Shape.FillProperty, "Brush.Threat.Amber");
                FillWedge.Visibility = Visibility.Visible;
                break;
            case "High":
                FillWedge.Data = ThreeQuarterWedge;
                FillWedge.SetResourceReference(Shape.FillProperty, "Brush.Threat.Amber");
                FillWedge.Visibility = Visibility.Visible;
                break;
            case "Very High":
                FillWedge.Data = ThreeQuarterWedge;
                FillWedge.SetResourceReference(Shape.FillProperty, "Brush.Threat.Amber");
                FillWedge.Visibility = Visibility.Visible;
                DangerRing.Visibility = Visibility.Visible;
                break;
            case "Extreme":
                FullFill.Visibility = Visibility.Visible;
                RingOutline.Visibility = Visibility.Collapsed;
                break;
            default:
                TextGlyph.Text = "?";
                RingOutline.Visibility = Visibility.Collapsed;
                break;
        }

        ToolTip = string.IsNullOrWhiteSpace(band) ? "Unk" : band;
    }
}
