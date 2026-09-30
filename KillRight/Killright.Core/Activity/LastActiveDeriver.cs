using Killright.Shared.zKill;

namespace Killright.Core.Activity;

public static class LastActiveDeriver
{
    public static (DateTimeOffset? LastActiveUtc, zKillActivityType? ActivityType) Derive(
        IReadOnlyDictionary<string, zKillStatisticsMonth>? months,
        DateTimeOffset today)
    {
        if (months is null || months.Count == 0)
            return (null, null);

        var latest = months.Values
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .FirstOrDefault();

        if (latest is null)
            return (null, null);

        var activityType = latest.ShipsDestroyed >= latest.ShipsLost
            ? zKillActivityType.Kill
            : zKillActivityType.Loss;

        return (MonthActivityDate(latest, today), activityType);
    }

    public static DateTimeOffset? DeriveLastKill(
        IReadOnlyDictionary<string, zKillStatisticsMonth>? months,
        DateTimeOffset today)
    {
        if (months is null || months.Count == 0)
            return null;

        var latestWithKills = months.Values
            .Where(m => m.ShipsDestroyed > 0)
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .FirstOrDefault();

        return latestWithKills is null ? null : MonthActivityDate(latestWithKills, today);
    }

    private static DateTimeOffset MonthActivityDate(zKillStatisticsMonth month, DateTimeOffset today)
    {
        var isCurrentMonth = month.Year == today.Year && month.Month == today.Month;

        var (previousYear, previousMonth) = PreviousMonth(today.Year, today.Month);
        var isEarlyInCurrentMonthForPreviousMonth =
            month.Year == previousYear && month.Month == previousMonth && today.Day <= 7;

        if (isCurrentMonth || isEarlyInCurrentMonthForPreviousMonth)
            return today.AddDays(-8);

        var lastDayOfMonth = DateTime.DaysInMonth(month.Year, month.Month);

        return new DateTimeOffset(month.Year, month.Month, lastDayOfMonth, 0, 0, 0, TimeSpan.Zero);
    }

    private static (int Year, int Month) PreviousMonth(int year, int month)
    {
        return month == 1 ? (year - 1, 12) : (year, month - 1);
    }
}
