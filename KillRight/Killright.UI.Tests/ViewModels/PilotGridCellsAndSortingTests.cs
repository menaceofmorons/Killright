using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Time;
using Killright.UI.Resources;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotGridCellsAndSortingTests
{
    private static Pilot ResolvedPilot() => new()
    {
        InputName = "Pilot One",
        CharacterId = 1,
        CharacterName = "Pilot One",
        VerifyStatus = VerifyStatus.Partial,
        CorporationId = 10,
        Corporation = new Corporation { CorporationId = 10, Name = "Corp" }
    };

    private static PilotReportRow Build(
        Pilot pilot,
        zKillActivity? activity = null,
        bool statisticsFailed = false,
        bool recentFailed = false,
        string? engineFailure = null,
        string threat = "1") =>
        PilotReportRowFactory.FromPilot(pilot, activity, null, StyleClassification.Solo, false, threat, statisticsFailed, recentFailed, engineFailure);

    [Fact]
    public void NoFailedCall_PilotIsNotItalicAndNotesEmpty()
    {
        var row = Build(ResolvedPilot());

        Assert.False(row.DataRetrievalFailed);
        Assert.Equal(string.Empty, row.Notes);
    }

    [Fact]
    public void StatisticsFailure_IsItalicAndNamedInNotes()
    {
        var row = Build(ResolvedPilot(), statisticsFailed: true);

        Assert.True(row.DataRetrievalFailed);
        Assert.Equal("zKillboard statistics", row.Notes);
    }

    [Fact]
    public void RecentFailure_IsItalicAndNamedInNotes()
    {
        var row = Build(ResolvedPilot(), recentFailed: true);

        Assert.True(row.DataRetrievalFailed);
        Assert.Equal("zKillboard recent killmails", row.Notes);
    }

    [Fact]
    public void EngineFailure_IsItalicAndNamedInNotes()
    {
        var row = Build(ResolvedPilot(), engineFailure: "missing_runtime");

        Assert.True(row.DataRetrievalFailed);
        Assert.Equal("engine", row.Notes);
    }

    [Fact]
    public void AffiliationFailure_IsItalicAndNamedInNotes()
    {
        var pilot = new Pilot { InputName = "P", CharacterId = 1, CharacterName = "P", VerifyStatus = VerifyStatus.Partial };

        var row = Build(pilot);

        Assert.True(row.DataRetrievalFailed);
        Assert.Equal("ESI", row.Notes);
    }

    [Fact]
    public void NoMatchPilot_IsNotAFailure()
    {
        var row = Build(new Pilot { InputName = "P", VerifyStatus = VerifyStatus.NoMatch });

        Assert.False(row.DataRetrievalFailed);
        Assert.Equal(string.Empty, row.Notes);
    }

    [Fact]
    public void SeveralFailures_NameOnlyFailedCalls()
    {
        var row = Build(ResolvedPilot(), statisticsFailed: true, engineFailure: "x");

        Assert.Equal("zKillboard statistics, engine", row.Notes);
    }

    [Fact]
    public void NullActivityWithoutFailedCall_DoesNotNameRecentKillmails()
    {
        var row = Build(ResolvedPilot(), activity: null);

        Assert.DoesNotContain("recent", row.Notes);
        Assert.Equal(string.Empty, row.Notes);
    }

    [Fact]
    public void ThreatSortKey_OrdersNoneToExtremeWithUnkLowest()
    {
        var keys = new[] { "Unk", "None", "1", "2", "3", "4", "5" }.Select(PilotGridSortKeys.Threat).ToList();

        Assert.Equal(keys.OrderBy(key => key).ToList(), keys);
        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }

    [Fact]
    public void SecuritySortKey_IsNumericAndUnkLowest()
    {
        Assert.True(PilotGridSortKeys.Security(-9.9) < PilotGridSortKeys.Security(-1.04));
        Assert.True(PilotGridSortKeys.Security(-1.04) < PilotGridSortKeys.Security(-1.0));
        Assert.True(PilotGridSortKeys.Security(null) < PilotGridSortKeys.Security(-10.0));
        Assert.True(PilotGridSortKeys.Security(10.0) > PilotGridSortKeys.Security(9.99));
    }

    [Fact]
    public void WeekSortKey_OrdersByKillsThenSolosWithMissingAsZero()
    {
        Assert.True(PilotGridSortKeys.Week(5, 0) > PilotGridSortKeys.Week(4, 4));
        Assert.True(PilotGridSortKeys.Week(4, 3) > PilotGridSortKeys.Week(4, 2));
        Assert.Equal(PilotGridSortKeys.Week(null, null), PilotGridSortKeys.Week(0, 0));
        Assert.True(PilotGridSortKeys.Week(10, 0) > PilotGridSortKeys.Week(9, 9));
    }

    [Fact]
    public void LastKillSortKey_OrdersByAgeAndDashIsOldest()
    {
        Assert.True(PilotGridSortKeys.LastKill(TimeSpan.FromHours(1)) < PilotGridSortKeys.LastKill(TimeSpan.FromDays(10)));
        Assert.True(PilotGridSortKeys.LastKill(TimeSpan.FromDays(400)) < PilotGridSortKeys.LastKill(null));
    }

    [Fact]
    public void Row_CarriesSortKeysFromValues()
    {
        var activity = new zKillActivity(1, true, 4, 2, null, null, DateTimeOffset.UtcNow, LastKillUtc: ApplicationClock.UtcNow.AddHours(-3));
        var pilot = ResolvedPilot() with { SecurityStatus = -1.04 };

        var row = Build(pilot, activity, threat: "None");

        Assert.Equal(0, row.ThreatSortKey);
        Assert.Equal(-1.04, row.SecuritySortKey);
        Assert.Equal(PilotGridSortKeys.Week(4, 2), row.WeekSortKey);
        Assert.InRange(row.LastKillSortKey, 3 * 3600 - 5, 3 * 3600 + 5);
    }

    [Fact]
    public void Row_WithoutValues_UsesLowestOrOldestKeys()
    {
        var row = Build(new Pilot { InputName = "P", VerifyStatus = VerifyStatus.NoMatch }, threat: "Unk");

        Assert.Equal(-1, row.ThreatSortKey);
        Assert.Equal(double.NegativeInfinity, row.SecuritySortKey);
        Assert.Equal(0, row.WeekSortKey);
        Assert.Equal(double.MaxValue, row.LastKillSortKey);
    }

    [Fact]
    public void LastKillAgeText_ComesFromResourceSet()
    {
        Assert.Equal("1h", PilotReportRowFactory.FormatKillAge(TimeSpan.FromMinutes(5)));
        Assert.Equal("10d", PilotReportRowFactory.FormatKillAge(TimeSpan.FromDays(10)));
        Assert.Equal(">30d", PilotReportRowFactory.FormatKillAge(TimeSpan.FromDays(40)));
    }

    [Fact]
    public void GridLiterals_ComeFromResourceSet()
    {
        Assert.Equal("unk", UiText.PlaceholderUnkLower);
        Assert.Equal("?", UiText.PlaceholderUnknownGlyph);
        Assert.Equal("V", UiText.GridVerifyVerified);
        Assert.Equal("P", UiText.GridVerifyPartial);
        Assert.Equal("(I) ", UiText.GridFilterIgnorePrefix);
        Assert.Equal("(F) ", UiText.GridFilterFilterPrefix);
        Assert.Equal(">30d", UiText.GridLastKillOver);
        Assert.Equal("5h", UiText.FormatLastKillHours(5));
        Assert.Equal("5d", UiText.FormatLastKillDays(5));
    }
}
