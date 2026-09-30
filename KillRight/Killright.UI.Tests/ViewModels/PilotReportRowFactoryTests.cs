using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.zKill;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotReportRowFactoryTests
{
    private static Pilot CreatePilot()
    {
        return new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial
        };
    }

    [Fact]
    public void FromPilot_KillsAndSolos_MatchWeekComponents()
    {
        var activity = new zKillActivity(95465499, true, 4, 2, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Solo, false, "Low", false, false, null);

        Assert.Equal("4/2", row.Week);
        Assert.Equal("4", row.Kills);
        Assert.Equal("2", row.Solos);
    }

    [Fact]
    public void FromPilot_WeekSideIsZero_DisplaysDashPerZeroDisplayRule()
    {
        var activity = new zKillActivity(95465499, true, 0, 2, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Solo, false, "Low", false, false, null);

        Assert.Equal("-/2", row.Week);
        Assert.Equal("0", row.Kills);
    }

    [Fact]
    public void FromPilot_StyleClassifications_ExposedAsFullWords()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.GangBeginner, false, "Unk", false, false, null);

        Assert.Equal("Gang(b)", row.RecentStyle);
        Assert.Equal("Unk", row.GeneralStyle);
    }

    [Fact]
    public void FromPilot_ZeroKillsZeroLosses_GeneralStyleInactiveAndLetterCodeI()
    {
        var statistics = new zKillStatistics { shipsDestroyed = 0, shipsLost = 0 };

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, statistics, StyleClassification.Solo, false, "Unk", false, false, null);

        Assert.Equal("Inactive", row.GeneralStyle);
        Assert.Equal("I/S", row.Style);
    }

    [Fact]
    public void FromPilot_NoHistoryPilot_ShowsInactiveInBothStyleSlots()
    {
        var statistics = new zKillStatistics { NoHistory = true };

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, statistics, StyleClassification.Inactive, false, "Unk", false, false, null);

        Assert.Equal("Inactive", row.GeneralStyle);
        Assert.Equal("I/I", row.Style);
    }

    [Fact]
    public void FromPilot_RecentIsPodder_AppendsSuffixToLetterCodeAndFullWord()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Solo, true, "Unk", false, false, null);

        Assert.Equal("U/Sx", row.Style);
        Assert.Equal("Solo (podder)", row.RecentStyle);
    }

    [Fact]
    public void FromPilot_RecentIsPodderButFleetStyle_NoSuffixApplied()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Fleet, true, "Unk", false, false, null);

        Assert.Equal("U/F", row.Style);
        Assert.Equal("Fleet", row.RecentStyle);
    }

    [Fact]
    public void FromPilot_CopiesInputNameFromPilot()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("T'ral Vsengne", row.InputName);
    }

    [Fact]
    public void PilotReportRow_DoesNotCarryBirthdayOrLastActivity()
    {
        Assert.Null(typeof(PilotReportRow).GetProperty("Birthday"));
        Assert.Null(typeof(PilotReportRow).GetProperty("LastActivity"));
    }

    [Fact]
    public void FromPilot_NoAllianceId_AllianceDisplayIsBlank()
    {
        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), null, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal(string.Empty, row.Alliance);
        Assert.Equal(string.Empty, row.AlliancePlain);
    }

    [Fact]
    public void FromPilot_AllianceLookupFailed_AllianceDisplayIsBlank()
    {
        var pilot = CreatePilot() with { AllianceId = 99000001 };

        var row = PilotReportRowFactory.FromPilot(
            pilot, null, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal(string.Empty, row.Alliance);
        Assert.Equal(string.Empty, row.AlliancePlain);
    }

    [Fact]
    public void FromPilot_AlliancePresent_AllianceAndPlainMatchName()
    {
        var pilot = CreatePilot() with
        {
            Alliance = new Alliance { AllianceId = 99000001, Name = "Federation" },
            AllianceId = 99000001
        };

        var row = PilotReportRowFactory.FromPilot(
            pilot, null, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("Federation", row.Alliance);
        Assert.Equal("Federation", row.AlliancePlain);
    }

    [Fact]
    public void FromPilot_CorporationPresent_PlainMatchesCorporationBeforeAnnotation()
    {
        var pilot = CreatePilot() with
        {
            Corporation = new Corporation { CorporationId = 2000001, Name = "Acme" }
        };

        var row = PilotReportRowFactory.FromPilot(
            pilot, null, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("Acme", row.Corporation);
        Assert.Equal("Acme", row.CorporationPlain);
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
            false,
            "Unk",
            statisticsCallFailed,
            recentCallFailed,
            engineFailed ? "engine boom" : null);

        Assert.Equal(expectedSource, row.StatsFailureSource);
    }

    [Fact]
    public void FromPilot_NoFailures_StatsFailureSourceIsNull()
    {
        var activity = new zKillActivity(95465499, true, 1, 0, null, null, DateTimeOffset.UtcNow);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Null(row.StatsFailureSource);
    }

    [Fact]
    public void FromPilot_LatestActivityIsLossButRecentKillExists_LastKillShowsKillAge()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, true, 1, 0, now.AddHours(-2), zKillActivityType.Loss, now, LastKillUtc: now.AddDays(-3));

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("3d", row.LastKill);
    }

    [Fact]
    public void FromPilot_LatestActivityIsLossAndNoKill_LastKillIsDash()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, true, 0, 0, now.AddHours(-2), zKillActivityType.Loss, now);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("-", row.LastKill);
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

    [Fact]
    public void FromPilot_LatestActivityIsKillAndLastKillIsSet_LastKillShowsTheStoredValue()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, true, 1, 0, now.AddHours(-4.5), zKillActivityType.Kill, now, LastKillUtc: now.AddHours(-4.5));

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("5h", row.LastKill);
    }

    [Fact]
    public void FromPilot_LastKillIsNull_LastKillIsDashWhateverLastActiveUtcIs()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, true, 1, 0, now.AddHours(-4.5), zKillActivityType.Kill, now);

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("-", row.LastKill);
    }

    [Fact]
    public void FromPilot_StoredLastKillOlderThanThirtyDaysAndLatestActivityIsLoss_LastKillShowsOverflowMarker()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, true, 0, 0, now.AddHours(-2), zKillActivityType.Loss, now, LastKillUtc: now.AddDays(-90));

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal(">30d", row.LastKill);
    }

    [Fact]
    public void FromPilot_LastKillKnownWithoutPublicActivityData_LastKillStillShowsTheAge()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new zKillActivity(
            95465499, false, 0, 0, null, null, now, LastKillUtc: now.AddDays(-12));

        var row = PilotReportRowFactory.FromPilot(
            CreatePilot(), activity, null, StyleClassification.Unknown, false, "Unk", false, false, null);

        Assert.Equal("12d", row.LastKill);
    }
}
