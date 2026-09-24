using Killright.Core.Activity;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Core.Tests.Activity;

public sealed class RecentCallSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculatePastSeconds_NoLastCall_ReturnsSevenDays()
    {
        var result = RecentCallScheduler.CalculatePastSeconds(null, Now);

        Assert.Equal(604800, result);
    }

    [Fact]
    public void CalculatePastSeconds_RoundsUpToWholeHour()
    {
        var lastCall = Now.AddHours(-2).AddMinutes(-10);

        var result = RecentCallScheduler.CalculatePastSeconds(lastCall, Now);

        Assert.Equal(0, result % 3600);
        Assert.True(result >= 3600 * 3);
    }

    [Fact]
    public void CalculatePastSeconds_VeryRecentCall_ClampsToMinimum()
    {
        var lastCall = Now.AddSeconds(-10);

        var result = RecentCallScheduler.CalculatePastSeconds(lastCall, Now);

        Assert.Equal(3600, result);
    }

    [Fact]
    public void CalculatePastSeconds_ExactlySevenDaysAgo_ClampsToMaximum()
    {
        var lastCall = Now.AddSeconds(-604800);

        var result = RecentCallScheduler.CalculatePastSeconds(lastCall, Now);

        Assert.Equal(604800, result);
    }

    [Fact]
    public void CalculatePastSeconds_OlderThanSevenDays_ReturnsSevenDays()
    {
        var lastCall = Now.AddDays(-30);

        var result = RecentCallScheduler.CalculatePastSeconds(lastCall, Now);

        Assert.Equal(604800, result);
    }

    [Fact]
    public void ShouldSkipForInterval_NoLastCall_ReturnsFalse()
    {
        Assert.False(RecentCallScheduler.ShouldSkipForInterval(null, Now));
    }

    [Fact]
    public void ShouldSkipForInterval_UnderOneHourAgo_ReturnsTrue()
    {
        var lastCall = Now.AddMinutes(-30);

        Assert.True(RecentCallScheduler.ShouldSkipForInterval(lastCall, Now));
    }

    [Fact]
    public void ShouldSkipForInterval_OverOneHourAgo_ReturnsFalse()
    {
        var lastCall = Now.AddMinutes(-61);

        Assert.False(RecentCallScheduler.ShouldSkipForInterval(lastCall, Now));
    }

    [Fact]
    public void ResolveCoverageStartUtc_NoRequestMade_ReturnsStoredValueUnchanged()
    {
        var stored = Now.AddDays(-10);

        var result = RecentCallScheduler.ResolveCoverageStartUtc(stored, Now.AddHours(-2), null, Now);

        Assert.Equal(stored, result);
    }

    [Fact]
    public void ResolveCoverageStartUtc_FirstSuccessfulCall_UsesRequestedWindowStart()
    {
        var result = RecentCallScheduler.ResolveCoverageStartUtc(null, null, 604800, Now);

        Assert.Equal(Now.AddSeconds(-604800), result);
    }

    [Fact]
    public void ResolveCoverageStartUtc_ContinuousWithPreviousCall_KeepsStoredValue()
    {
        var stored = Now.AddDays(-10);
        var previousCall = Now.AddSeconds(-3600);

        var result = RecentCallScheduler.ResolveCoverageStartUtc(stored, previousCall, 3900, Now);

        Assert.Equal(stored, result);
    }

    [Fact]
    public void ResolveCoverageStartUtc_GapSincePreviousCall_ResetsToWindowStart()
    {
        var stored = Now.AddDays(-10);
        var previousCall = Now.AddDays(-8);

        var result = RecentCallScheduler.ResolveCoverageStartUtc(stored, previousCall, 604800, Now);

        Assert.Equal(Now.AddSeconds(-604800), result);
    }

    [Fact]
    public void ShouldShortCircuit_NullMonths_ReturnsTrue()
    {
        Assert.True(RecentCallScheduler.ShouldShortCircuit(null, Now));
    }

    [Fact]
    public void ShouldShortCircuit_CurrentMonthHasData_ReturnsFalse()
    {
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202609"] = new() { Year = 2026, Month = 9, ShipsDestroyed = 1 }
        };

        Assert.False(RecentCallScheduler.ShouldShortCircuit(months, Now));
    }

    [Fact]
    public void ShouldShortCircuit_OnOrAfterEighth_NoCurrentMonthData_ReturnsTrue()
    {
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 1 }
        };

        Assert.True(RecentCallScheduler.ShouldShortCircuit(months, Now));
    }

    [Fact]
    public void ShouldShortCircuit_OnOrBeforeSeventh_PreviousMonthHasData_ReturnsFalse()
    {
        var today = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 1 }
        };

        Assert.False(RecentCallScheduler.ShouldShortCircuit(months, today));
    }

    [Fact]
    public void ShouldShortCircuit_OnOrBeforeSeventh_NeitherMonthHasData_ReturnsTrue()
    {
        var today = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202606"] = new() { Year = 2026, Month = 6, ShipsDestroyed = 1 }
        };

        Assert.True(RecentCallScheduler.ShouldShortCircuit(months, today));
    }

    [Fact]
    public void ShouldShortCircuit_MonthRolloverAcrossYearBoundary_UsesPreviousDecember()
    {
        var today = new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202612"] = new() { Year = 2026, Month = 12, ShipsDestroyed = 1 }
        };

        Assert.False(RecentCallScheduler.ShouldShortCircuit(months, today));
    }
}
