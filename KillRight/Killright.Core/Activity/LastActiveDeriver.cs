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

        var isCurrentMonth = latest.Year == today.Year && latest.Month == today.Month;

        var (previousYear, previousMonth) = PreviousMonth(today.Year, today.Month);
        var isEarlyInCurrentMonthForPreviousMonth =
            latest.Year == previousYear && latest.Month == previousMonth && today.Day <= 7;

        if (isCurrentMonth || isEarlyInCurrentMonthForPreviousMonth)
            return (today.AddDays(-8), activityType);

        var lastDayOfMonth = DateTime.DaysInMonth(latest.Year, latest.Month);
        var derivedDate = new DateTimeOffset(latest.Year, latest.Month, lastDayOfMonth, 0, 0, 0, TimeSpan.Zero);

        return (derivedDate, activityType);
    }

    private static (int Year, int Month) PreviousMonth(int year, int month)
    {
        return month == 1 ? (year - 1, 12) : (year, month - 1);
    }
}
