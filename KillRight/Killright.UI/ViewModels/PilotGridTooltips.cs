using System.Globalization;
using System.Windows.Data;

namespace Killright.UI.ViewModels;

public static class PilotGridTooltips
{
    public static string? Style(string generalStyle, string recentStyle)
    {
        if (string.IsNullOrWhiteSpace(generalStyle) && string.IsNullOrWhiteSpace(recentStyle))
            return null;

        return $"{generalStyle} / {recentStyle}";
    }

    public static string? FactionName(long? factionId, Func<long, string?> lookupFactionName)
    {
        if (factionId is not { } id || Resources.UiText.FactionShortCode(id) is null)
            return null;

        var name = lookupFactionName(id);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

public sealed class FactionNameTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PilotGridTooltips.FactionName(value as long?, id => App.SdeReferenceDataStore?.GetFactionName(id));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
