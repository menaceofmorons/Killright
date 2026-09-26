using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotReportRowFactoryTests
{
    [Theory]
    [InlineData(0.2, "1h")]
    [InlineData(3.01, "4h")]
    [InlineData(23.99, "24h")]
    public void FormatKillAge_SubDayAge_RoundsUpToWholeHoursMinimumOne(double hours, string expected)
    {
        Assert.Equal(expected, PilotReportRowFactory.FormatKillAge(TimeSpan.FromHours(hours)));
    }

    [Theory]
    [InlineData(1.0, "1d")]
    [InlineData(29.9, "29d")]
    public void FormatKillAge_UnderThirtyDays_ReturnsWholeDays(double days, string expected)
    {
        Assert.Equal(expected, PilotReportRowFactory.FormatKillAge(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void FormatKillAge_ThirtyDaysOrOlder_ReturnsOverflowMarker()
    {
        Assert.Equal(">30d", PilotReportRowFactory.FormatKillAge(TimeSpan.FromDays(30)));
    }
}
