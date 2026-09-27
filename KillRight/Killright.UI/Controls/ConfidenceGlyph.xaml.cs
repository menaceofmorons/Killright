using System.Windows;
using System.Windows.Controls;

namespace Killright.UI.Controls;

public partial class ConfidenceGlyph : UserControl
{
    public static readonly DependencyProperty ConfidenceProperty = DependencyProperty.Register(
        nameof(Confidence),
        typeof(string),
        typeof(ConfidenceGlyph),
        new PropertyMetadata(string.Empty, OnConfidenceChanged));

    public ConfidenceGlyph()
    {
        InitializeComponent();
        Render(Confidence);
    }

    public string Confidence
    {
        get => (string)GetValue(ConfidenceProperty);
        set => SetValue(ConfidenceProperty, value);
    }

    private static void OnConfidenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ConfidenceGlyph)d).Render((string)e.NewValue);
    }

    private void Render(string confidence)
    {
        RingOutline.Visibility = Visibility.Collapsed;
        BottomHalfFill.Visibility = Visibility.Collapsed;
        FullFill.Visibility = Visibility.Collapsed;
        ToolTip = null;

        switch (confidence)
        {
            case "Low":
                RingOutline.Visibility = Visibility.Visible;
                break;
            case "Medium":
                RingOutline.Visibility = Visibility.Visible;
                BottomHalfFill.Visibility = Visibility.Visible;
                break;
            case "High":
                FullFill.Visibility = Visibility.Visible;
                break;
            case null or "":
                break;
            default:
                RingOutline.Visibility = Visibility.Visible;
                ToolTip = confidence;
                break;
        }
    }
}
