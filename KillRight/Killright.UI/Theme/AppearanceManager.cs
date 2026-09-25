using System.Windows;
using Killright.UI.UiState;

namespace Killright.UI.Theme;

public static class AppearanceManager
{
    private static ResourceDictionary? _themeDictionary;
    private static ResourceDictionary? _fontTierDictionary;

    public static void ApplyTheme(AppTheme requested)
    {
        var resolved = ThemeResolver.Resolve(requested, OsThemeReader.IsLightTheme());
        var dictionary = new ResourceDictionary { Source = AppearanceResourceMap.ThemeDictionaryUri(resolved) };
        Replace(ref _themeDictionary, dictionary);
    }

    public static void ApplyFontTier(GridFontTier tier)
    {
        var dictionary = new ResourceDictionary { Source = AppearanceResourceMap.FontTierDictionaryUri(tier) };
        Replace(ref _fontTierDictionary, dictionary);
    }

    private static void Replace(ref ResourceDictionary? previous, ResourceDictionary next)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        if (previous is not null)
        {
            var index = merged.IndexOf(previous);

            if (index >= 0)
            {
                merged[index] = next;
                previous = next;
                return;
            }
        }

        merged.Add(next);
        previous = next;
    }
}
