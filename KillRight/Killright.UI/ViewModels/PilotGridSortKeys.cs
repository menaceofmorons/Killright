using System.Globalization;

namespace Killright.UI.ViewModels;

public static class PilotGridSortKeys
{
    private const long WeekKillsMultiplier = 1_000_000_000L;

    public static int Threat(string? band)
    {
        if (band == "None")
            return 0;

        return int.TryParse(band, NumberStyles.None, CultureInfo.InvariantCulture, out var position) && position is >= 1 and <= 5
            ? position
            : -1;
    }

    public static double Security(double? securityStatus) =>
        securityStatus is { } value && double.IsFinite(value) ? value : double.NegativeInfinity;

    public static long Week(int? kills, int? solos) =>
        Math.Max(0, kills ?? 0) * WeekKillsMultiplier + Math.Max(0, solos ?? 0);

    public static double LastKill(TimeSpan? age) =>
        age is { } value ? Math.Max(0d, value.TotalSeconds) : double.MaxValue;
}
