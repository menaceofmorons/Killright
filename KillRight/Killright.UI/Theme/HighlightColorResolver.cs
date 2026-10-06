using System.Windows.Media;
using Killright.UI.MenuModal;
using Killright.UI.UiState;

namespace Killright.UI.Theme;

public static class HighlightColorResolver
{
    public static string ResolveHex(string hex, AppTheme resolvedTheme)
    {
        if (resolvedTheme != AppTheme.Light)
            return hex;

        foreach (var swatch in HighlightSwatchPalette.Swatches)
        {
            if (string.Equals(swatch.DarkHex, hex, StringComparison.OrdinalIgnoreCase))
                return swatch.LightHex;
        }

        return hex;
    }

    public static bool TryResolveColor(string? hex, AppTheme resolvedTheme, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(ResolveHex(hex, resolvedTheme))!;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
