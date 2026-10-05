using System.Windows;
using System.Windows.Media;
using Killright.UI.UiState;
using Killright.UI.ViewModels;

namespace Killright.UI.Theme;

public static class AppearanceManager
{
    public const string NewPilotBrushKey = "Brush.Row.NewPilot";

    private static ResourceDictionary? _themeDictionary;
    private static ResourceDictionary? _fontTierDictionary;

    public static void ApplyTheme(AppTheme requested)
    {
        var resolved = ThemeResolver.Resolve(requested, OsThemeReader.IsLightTheme());
        var dictionary = new ResourceDictionary { Source = AppearanceResourceMap.ThemeDictionaryUri(resolved) };
        Replace(ref _themeDictionary, dictionary);
    }

    public static void ApplyNewPilotColor(string? hex, double highlightOpacity)
    {
        var resources = Application.Current.Resources;

        if (TryParseColor(hex, out var color))
            resources[NewPilotBrushKey] = new SolidColorBrush(HighlightColorCalculator.ForNewPilot(color, highlightOpacity));
        else
            resources.Remove(NewPilotBrushKey);
    }

    public static void ApplyFontTier(GridFontTier tier)
    {
        var dictionary = new ResourceDictionary { Source = AppearanceResourceMap.FontTierDictionaryUri(tier) };
        Replace(ref _fontTierDictionary, dictionary);
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex)!;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
