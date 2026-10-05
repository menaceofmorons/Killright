using System.Windows.Media;

namespace Killright.UI.ViewModels;

public static class HighlightColorCalculator
{
    private const double SameGroupLightnessDelta = 0.25;

    public static Color ForPilot(Color baseColor, double opacity) => ApplyOpacity(baseColor, opacity);

    public static Color ForSameGroup(Color baseColor, double opacity) => ApplyOpacity(Lighten(baseColor, SameGroupLightnessDelta), opacity);

    public static Color ForRelated(Color baseColor, double opacity) => ApplyOpacity(baseColor, opacity);

    public static Color ForNewPilot(Color baseColor, double opacity) => ApplyOpacity(baseColor, opacity);

    private static Color ApplyOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0d, 1d) * 255d);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color Lighten(Color color, double lightnessDelta)
    {
        var (h, s, l) = ToHsl(color);
        l = Math.Clamp(l + lightnessDelta, 0d, 1d);
        return FromHsl(h, s, l, color.A);
    }

    private static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2d;
        double h = 0d;
        double s = 0d;

        if (max != min)
        {
            var delta = max - min;
            s = l > 0.5 ? delta / (2d - max - min) : delta / (max + min);

            if (max == r)
                h = (g - b) / delta + (g < b ? 6d : 0d);
            else if (max == g)
                h = (b - r) / delta + 2d;
            else
                h = (r - g) / delta + 4d;

            h /= 6d;
        }

        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l, byte alpha)
    {
        double r, g, b;

        if (s == 0d)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1d + s) : l + s - l * s;
            var p = 2d * l - q;
            r = HueToRgb(p, q, h + 1d / 3d);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1d / 3d);
        }

        return Color.FromArgb(alpha, ToByte(r), ToByte(g), ToByte(b));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0d) t += 1d;
        if (t > 1d) t -= 1d;
        if (t < 1d / 6d) return p + (q - p) * 6d * t;
        if (t < 1d / 2d) return q;
        if (t < 2d / 3d) return p + (q - p) * (2d / 3d - t) * 6d;
        return p;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d);
}
