using Killright.Shared.zKill;

namespace Killright.Core.Activity;

public static class RecentCallScheduler
{
    private const int MinimumWindowSeconds = 3600;
    private const int MaximumWindowSeconds = 604800;
    private const int OverlapSeconds = 300;
    private const int SevenDaysSeconds = 7 * 24 * 60 * 60;

    public static bool ShouldSkipForInterval(DateTimeOffset? lastSuccessfulCallUtc, DateTimeOffset now)
    {
        if (lastSuccessfulCallUtc is null)
            return false;

        return now - lastSuccessfulCallUtc.Value < TimeSpan.FromHours(1);
    }

    public static int CalculatePastSeconds(DateTimeOffset? lastSuccessfulCallUtc, DateTimeOffset now)
    {
        if (lastSuccessfulCallUtc is null)
            return SevenDaysSeconds;

        var elapsedSeconds = (int)Math.Ceiling((now - lastSuccessfulCallUtc.Value).TotalSeconds);

        if (elapsedSeconds > SevenDaysSeconds)
            return SevenDaysSeconds;

        var requiredSeconds = elapsedSeconds + OverlapSeconds;
        var hours = (int)Math.Ceiling(requiredSeconds / 3600d);

        return Math.Clamp(hours * 3600, MinimumWindowSeconds, MaximumWindowSeconds);
    }

    public static DateTimeOffset? ResolveCoverageStartUtc(
        DateTimeOffset? storedCoverageStartUtc,
        DateTimeOffset? previousSuccessfulCallUtc,
        int? pastSecondsRequested,
        DateTimeOffset now)
    {
        if (pastSecondsRequested is not int seconds)
            return storedCoverageStartUtc;

        var windowStartUtc = now - TimeSpan.FromSeconds(seconds);

        var isFirstSuccessfulCall = storedCoverageStartUtc is null;
        var leavesAGap = previousSuccessfulCallUtc is null || windowStartUtc > previousSuccessfulCallUtc;

        return isFirstSuccessfulCall || leavesAGap
            ? windowStartUtc
            : storedCoverageStartUtc;
    }

    public static bool ShouldShortCircuit(IReadOnlyDictionary<string, zKillStatisticsMonth>? months, DateTimeOffset today)
    {
        var hasCurrentMonthData = HasDataForMonth(months, today.Year, today.Month);

        if (today.Day >= 8)
            return !hasCurrentMonthData;

        var (previousYear, previousMonth) = PreviousMonth(today.Year, today.Month);
        var hasPreviousMonthData = HasDataForMonth(months, previousYear, previousMonth);

        return !hasCurrentMonthData && !hasPreviousMonthData;
    }

    private static bool HasDataForMonth(IReadOnlyDictionary<string, zKillStatisticsMonth>? months, int year, int month)
    {
        if (months is null)
            return false;

        return months.Values.Any(m => m.Year == year && m.Month == month);
    }

    private static (int Year, int Month) PreviousMonth(int year, int month)
    {
        return month == 1 ? (year - 1, 12) : (year, month - 1);
    }
}
