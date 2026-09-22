using Killright.Core.Activity;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Core.Tests.Activity;

public sealed class LastActiveDeriverTests
{
    [Fact]
    public void Derive_NullMonths_ReturnsNull()
    {
        var (lastActiveUtc, activityType) = LastActiveDeriver.Derive(null, new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(lastActiveUtc);
        Assert.Null(activityType);
    }

    [Fact]
    public void Derive_EmptyMonths_ReturnsNull()
    {
        var (lastActiveUtc, activityType) = LastActiveDeriver.Derive(
            new Dictionary<string, zKillStatisticsMonth>(),
            new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(lastActiveUtc);
        Assert.Null(activityType);
    }

    [Fact]
    public void Derive_LatestMonthIsCurrentMonth_ReturnsEightDaysBeforeToday()
    {
        var today = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202609"] = new() { Year = 2026, Month = 9, ShipsDestroyed = 3, ShipsLost = 1 }
        };

        var (lastActiveUtc, activityType) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(today.AddDays(-8), lastActiveUtc);
        Assert.Equal(zKillActivityType.Kill, activityType);
    }

    [Fact]
    public void Derive_PreviousMonthAndEarlyInCurrentMonth_ReturnsEightDaysBeforeToday()
    {
        var today = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 2, ShipsLost = 0 }
        };

        var (lastActiveUtc, _) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(today.AddDays(-8), lastActiveUtc);
    }

    [Fact]
    public void Derive_PreviousMonthButPastSeventhOfCurrentMonth_ReturnsLastDayOfThatMonth()
    {
        var today = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 2, ShipsLost = 0 }
        };

        var (lastActiveUtc, _) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), lastActiveUtc);
    }

    [Fact]
    public void Derive_OlderMonth_ReturnsLastDayOfThatMonth()
    {
        var today = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202606"] = new() { Year = 2026, Month = 6, ShipsDestroyed = 5, ShipsLost = 5 }
        };

        var (lastActiveUtc, activityType) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), lastActiveUtc);
        Assert.Equal(zKillActivityType.Kill, activityType);
    }

    [Fact]
    public void Derive_LossesExceedKillsInLatestMonth_ReturnsLoss()
    {
        var today = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202609"] = new() { Year = 2026, Month = 9, ShipsDestroyed = 1, ShipsLost = 5 }
        };

        var (_, activityType) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(zKillActivityType.Loss, activityType);
    }

    [Fact]
    public void Derive_MultipleMonths_PicksChronologicallyLatest()
    {
        var today = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["202601"] = new() { Year = 2026, Month = 1, ShipsDestroyed = 1, ShipsLost = 0 },
            ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 9, ShipsLost = 0 }
        };

        var (lastActiveUtc, _) = LastActiveDeriver.Derive(months, today);

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), lastActiveUtc);
    }
}
