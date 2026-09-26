using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotReportRowFactoryTests
{
    private static Pilot CreatePilot(DateOnly? birthday = null)
    {
        return new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            Birthday = birthday
        };
    }

    [Fact]
    public void FromPilot_KillsAndSolos_MatchWeekComponents()
    {
        var activity = new zKillActivity(95465499, true, 4, 2, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Solo, "Low", false, false, null, null, null);

        Assert.Equal("4/2", row.Week);
        Assert.Equal("4", row.Kills);
        Assert.Equal("2", row.Solos);
    }

    [Fact]
    public void FromPilot_StyleClassifications_ExposedAsFullWords()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.GangBeginner, "Unk", false, false, null, null, null);

        Assert.Equal("Gang(b)", row.RecentStyle);
        Assert.Equal("Unk", row.GeneralStyle);
    }

    [Fact]
    public void FromPilot_BirthdayPresent_FormatsAsIsoDate()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(new DateOnly(2015, 6, 12)), null, null, StyleClassification.Unknown, "Unk", false, false, null, new DateOnly(2015, 6, 12), null);

        Assert.Equal("2015-06-12", row.Birthday);
    }

    [Fact]
    public void FromPilot_BirthdayAbsent_FormatsAsUnk()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, "Unk", false, false, null, null, null);

        Assert.Equal("unk", row.Birthday);
    }

    [Theory]
    [InlineData(true, false, false, "killright_engine")]
    [InlineData(false, true, false, "zKill statistics")]
    [InlineData(false, false, true, "zKill recent killmails")]
    public void FromPilot_FailureFlags_ClassifyStatsFailureSource(bool engineFailed, bool statisticsCallFailed, bool recentCallFailed, string expectedSource)
    {
        var activity = new zKillActivity(95465499, true, 1, 0, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(),
            activity,
            null,
            StyleClassification.Unknown,
            "Unk",
            statisticsCallFailed,
            recentCallFailed,
            engineFailed ? "engine boom" : null,
            null,
            null);

        Assert.Equal(expectedSource, row.StatsFailureSource);
    }

    [Fact]
    public void FromPilot_NoFailures_StatsFailureSourceIsNull()
    {
        var activity = new zKillActivity(95465499, true, 1, 0, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, "Unk", false, false, null, null, null);

        Assert.Null(row.StatsFailureSource);
    }

    [Fact]
    public void FromPilot_LastActivityIsKill_PopulatesKillOnlyFields()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 30000142, 11567, 587, 3);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, "Unk", false, false, null, null, lastActivity);

        Assert.NotNull(row.LastActivity);
        Assert.True(row.LastActivity!.IsKill);
        Assert.Equal("Kill", row.LastActivity.KillLoss);
        Assert.Equal("11567", row.LastActivity.Ship);
        Assert.Equal("587", row.LastActivity.Victim);
        Assert.Equal("3", row.LastActivity.Attackers);
        Assert.Equal("—", row.LastActivity.System);
        Assert.Equal("—", row.LastActivity.Weapon);
    }

    [Fact]
    public void FromPilot_LastActivityIsLoss_OmitsKillOnlyFields()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Loss, 30000142, 587, null, null);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, "Unk", false, false, null, null, lastActivity);

        Assert.NotNull(row.LastActivity);
        Assert.False(row.LastActivity!.IsKill);
        Assert.Equal("Loss", row.LastActivity.KillLoss);
        Assert.Equal("587", row.LastActivity.Ship);
    }

    [Fact]
    public void FromPilot_NoLastActivity_LeavesLastActivityNull()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, "Unk", false, false, null, null, null);

        Assert.Null(row.LastActivity);
    }

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
