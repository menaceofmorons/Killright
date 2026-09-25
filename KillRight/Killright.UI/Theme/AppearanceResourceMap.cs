using Killright.UI.UiState;

namespace Killright.UI.Theme;

public static class AppearanceResourceMap
{
    public static Uri ThemeDictionaryUri(AppTheme resolvedTheme)
    {
        var fileName = resolvedTheme switch
        {
            AppTheme.Light => "Brushes.Light.xaml",
            AppTheme.Dark => "Brushes.Dark.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(resolvedTheme), resolvedTheme, "Theme must already be resolved to Light or Dark.")
        };

        return PackUri(fileName);
    }

    public static Uri FontTierDictionaryUri(GridFontTier tier)
    {
        var fileName = tier switch
        {
            GridFontTier.Large => "FontTier.Large.xaml",
            GridFontTier.Medium => "FontTier.Medium.xaml",
            GridFontTier.Small => "FontTier.Small.xaml",
            GridFontTier.Tiny => "FontTier.Tiny.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
        };

        return PackUri(fileName);
    }

    private static Uri PackUri(string fileName) =>
        new($"/Killright.UI;component/Theme/{fileName}", UriKind.Relative);
}
