namespace Killright.UI.Configuration;

internal static class SettingsAdjustment
{
    public static int Limit(string name, int value, int minimum, int? maximum, Action<string> log)
    {
        if (value < minimum)
        {
            log($"Setting '{name}' read as {value}; using {minimum} (below the minimum of {minimum}).");

            return minimum;
        }

        if (maximum is int upper && value > upper)
        {
            log($"Setting '{name}' read as {value}; using {upper} (above the maximum of {upper}).");

            return upper;
        }

        return value;
    }

    public static double SizeOrDefault(string name, double value, double minimum, double defaultValue, Action<string> log)
    {
        if (double.IsFinite(value) && value >= minimum)
            return value;

        log($"Setting '{name}' read as {value}; using the default {defaultValue} (not a size of at least {minimum}).");

        return defaultValue;
    }

    public static void LogMissing(string name, string defaultText, Action<string> log)
    {
        log($"Setting '{name}' read as missing; using the default {defaultText}.");
    }

    public static void LogBandsReplaced(string name, IEnumerable<(int Minimum, int Maximum)>? bands, string reason, Action<string> log)
    {
        log($"Setting '{name}' read as {DescribeBands(bands)}; using the built-in bands ({reason}).");
    }

    private static string DescribeBands(IEnumerable<(int Minimum, int Maximum)>? bands)
    {
        if (bands is null)
            return "null";

        return "[" + string.Join(", ", bands.Select(band => $"{band.Minimum}-{band.Maximum}")) + "]";
    }
}
