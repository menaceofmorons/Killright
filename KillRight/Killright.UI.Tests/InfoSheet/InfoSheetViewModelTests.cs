using System.Windows;
using Killright.UI.InfoSheet;
using Killright.UI.Resources;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.InfoSheet;

public sealed class InfoSheetViewModelTests
{
    private static PilotReportRow CreateRow(long? characterId = 95465499, string? statsFailureSource = null)
    {
        return new PilotReportRow
        {
            CharacterId = characterId,
            InputName = "T'ral Vsengne",
            Pilot = "T'ral Vsengne",
            Threat = "Low",
            SecurityStatus = "1.23",
            InfoWeekKills = "4",
            InfoWeekSolos = "2",
            InfoWeekLosses = "-",
            GeneralStyle = "Solo",
            RecentStyle = "Gang",
            StatsFailureSource = statsFailureSource
        };
    }

    private static PilotLastActivitySummary KillSummary() => new()
    {
        DateTime = "2026-01-02 03:04",
        KillLoss = "Kill",
        System = "Jita",
        Ship = "Crow",
        Weapon = "Light Missile Launcher",
        Victim = "Rifter",
        Attackers = "3",
        IsKill = true
    };

    [Fact]
    public void BeforeLoad_BirthdayAndLastActivityShowLoadingPlaceholder()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => new TaskCompletionSource<DateOnly?>().Task,
            _ => new TaskCompletionSource<PilotLastActivitySummary?>().Task);

        Assert.Equal(UiText.InfoSheetLoading, viewModel.BirthdayValue);
        Assert.Equal(UiText.InfoSheetLoading, viewModel.LastActivityDateTimeValue);
        Assert.Equal(UiText.InfoSheetLoading, viewModel.LastActivityKillLossValue);
        Assert.Equal(UiText.InfoSheetLoading, viewModel.LastActivitySystemValue);
        Assert.Equal(UiText.InfoSheetLoading, viewModel.LastActivityShipValue);
        Assert.Equal(Visibility.Collapsed, viewModel.LastActivityKillOnlyVisibility);
        Assert.Equal("Low", viewModel.ThreatValue);
        Assert.Equal("1.23", viewModel.SecValue);
        Assert.Equal("4", viewModel.WeekKillsValue);
        Assert.Equal("2", viewModel.WeekSolosValue);
        Assert.Equal("-", viewModel.WeekLossesValue);
        Assert.Equal(UiText.InfoSheetLoading, viewModel.TotalKillsValue);
    }

    [Fact]
    public async Task LoadAsync_UsesInputNameAndCharacterId_AndShowsBirthdayAndLastActivity()
    {
        string? requestedName = null;
        long? requestedId = null;

        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            name =>
            {
                requestedName = name;
                return Task.FromResult<DateOnly?>(new DateOnly(2015, 6, 12));
            },
            id =>
            {
                requestedId = id;
                return Task.FromResult<PilotLastActivitySummary?>(KillSummary());
            });

        await viewModel.LoadAsync();

        Assert.Equal("T'ral Vsengne", requestedName);
        Assert.Equal(95465499, requestedId);
        Assert.Equal("2015-06-12", viewModel.BirthdayValue);
        Assert.Equal("2026-01-02 03:04", viewModel.LastActivityDateTimeValue);
        Assert.Equal("Kill", viewModel.LastActivityKillLossValue);
        Assert.Equal("Jita", viewModel.LastActivitySystemValue);
        Assert.Equal("Crow", viewModel.LastActivityShipValue);
        Assert.Equal("Light Missile Launcher", viewModel.LastActivityWeaponValue);
        Assert.Equal("Rifter", viewModel.LastActivityVictimValue);
        Assert.Equal("3", viewModel.LastActivityAttackersValue);
        Assert.Equal(Visibility.Visible, viewModel.LastActivityKillOnlyVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_NoBirthdayAndNoLastActivity_ShowsUnkAndDashes()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal("unk", viewModel.BirthdayValue);
        Assert.Equal("-", viewModel.LastActivityDateTimeValue);
        Assert.Equal("-", viewModel.LastActivityShipValue);
        Assert.Equal(Visibility.Collapsed, viewModel.LastActivityKillOnlyVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_LossSummary_HidesKillOnlyFields()
    {
        var loss = new PilotLastActivitySummary
        {
            DateTime = "2026-01-02 03:04",
            KillLoss = "Loss",
            System = "Jita",
            Ship = "Rifter",
            Weapon = "—",
            Victim = "—",
            Attackers = "—",
            IsKill = false
        };

        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(loss));

        await viewModel.LoadAsync();

        Assert.Equal("Loss", viewModel.LastActivityKillLossValue);
        Assert.Equal(Visibility.Collapsed, viewModel.LastActivityKillOnlyVisibility);
    }

    [Fact]
    public async Task LoadAsync_RowWithoutCharacterId_SkipsLastActivityRead()
    {
        var lastActivityCalls = 0;

        var viewModel = new InfoSheetViewModel(
            CreateRow(characterId: null),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            _ =>
            {
                lastActivityCalls++;
                return Task.FromResult<PilotLastActivitySummary?>(null);
            });

        await viewModel.LoadAsync();

        Assert.Equal(0, lastActivityCalls);
        Assert.Equal("-", viewModel.LastActivityDateTimeValue);
    }

    [Fact]
    public async Task LoadAsync_BirthdayReadFails_ShowsErrorLineAndKeepsLastActivity()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => throw new InvalidOperationException("read failed"),
            _ => Task.FromResult<PilotLastActivitySummary?>(KillSummary()));

        await viewModel.LoadAsync();

        Assert.Equal(UiText.FormatErrorLoading(UiText.InfoSheetSourceBirthday), viewModel.ErrorLine);
        Assert.Equal(Visibility.Visible, viewModel.ErrorLineVisibility);
        Assert.Equal("unk", viewModel.BirthdayValue);
        Assert.Equal("Crow", viewModel.LastActivityShipValue);
    }

    [Fact]
    public async Task LoadAsync_LastActivityReadFails_ShowsErrorLineAndDashes()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(new DateOnly(2015, 6, 12)),
            _ => Task.FromException<PilotLastActivitySummary?>(new InvalidOperationException("read failed")));

        await viewModel.LoadAsync();

        Assert.Equal(UiText.FormatErrorLoading(UiText.InfoSheetSourceLastActivity), viewModel.ErrorLine);
        Assert.Equal(Visibility.Visible, viewModel.ErrorLineVisibility);
        Assert.Equal("2015-06-12", viewModel.BirthdayValue);
        Assert.Equal("-", viewModel.LastActivityDateTimeValue);
    }

    [Fact]
    public async Task LoadAsync_BothReadsFailWithExistingStatsFailure_ListsAllSourcesInOneLine()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(statsFailureSource: "zKill statistics"),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => throw new InvalidOperationException("read failed"),
            _ => throw new InvalidOperationException("read failed"));

        await viewModel.LoadAsync();

        Assert.Equal(
            UiText.FormatErrorLoading($"zKill statistics, {UiText.InfoSheetSourceBirthday}, {UiText.InfoSheetSourceLastActivity}"),
            viewModel.ErrorLine);
        Assert.Equal(Visibility.Visible, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_ValuesChange_RaisesPropertyChanged()
    {
        var changed = new List<string?>();

        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(new DateOnly(2015, 6, 12)),
            _ => Task.FromResult<PilotLastActivitySummary?>(KillSummary()));

        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.LoadAsync();

        Assert.Contains(nameof(InfoSheetViewModel.BirthdayValue), changed);
        Assert.Contains(nameof(InfoSheetViewModel.LastActivityShipValue), changed);
        Assert.Contains(nameof(InfoSheetViewModel.LastActivityKillOnlyVisibility), changed);
    }

    [Fact]
    public async Task LoadAsync_Totals_ShowKillsSolosAndLosses()
    {
        long? requestedId = null;

        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            id =>
            {
                requestedId = id;
                return Task.FromResult<InfoSheetTotals?>(new InfoSheetTotals(1200, 300, 45));
            },
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal(95465499, requestedId);
        Assert.Equal("1200", viewModel.TotalKillsValue);
        Assert.Equal("300", viewModel.TotalSolosValue);
        Assert.Equal("45", viewModel.TotalLossesValue);
        Assert.Equal(Visibility.Collapsed, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_ZeroTotals_ShowDash()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(new InfoSheetTotals(10, 0, 0)),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal("10", viewModel.TotalKillsValue);
        Assert.Equal("-", viewModel.TotalSolosValue);
        Assert.Equal("-", viewModel.TotalLossesValue);
    }

    [Fact]
    public async Task LoadAsync_NoStatisticsRow_ShowsDashesWithoutErrorLine()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal("-", viewModel.TotalKillsValue);
        Assert.Equal("-", viewModel.TotalSolosValue);
        Assert.Equal("-", viewModel.TotalLossesValue);
        Assert.Equal(Visibility.Collapsed, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_TotalsReadFails_ShowsDashesAndErrorLine()
    {
        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
            _ => Task.FromException<InfoSheetTotals?>(new InvalidOperationException("read failed")),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal("-", viewModel.TotalKillsValue);
        Assert.Equal(UiText.FormatErrorLoading(UiText.InfoSheetSourceStatistics), viewModel.ErrorLine);
        Assert.Equal(Visibility.Visible, viewModel.ErrorLineVisibility);
    }

    [Fact]
    public async Task LoadAsync_RowWithoutCharacterId_SkipsTotalsRead()
    {
        var totalsCalls = 0;

        var viewModel = new InfoSheetViewModel(
            CreateRow(characterId: null),
            false,
            _ =>
            {
                totalsCalls++;
                return Task.FromResult<InfoSheetTotals?>(null);
            },
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null));

        await viewModel.LoadAsync();

        Assert.Equal(0, totalsCalls);
        Assert.Equal("-", viewModel.TotalKillsValue);
    }

    [Fact]
    public void Layout_OrdersBirthdayThreatSecGeneralRecentThenTable()
    {
        var xaml = File.ReadAllText(FindInfoSheetXaml());

        var birthday = xaml.IndexOf("InfoSheetLabelBirthday", StringComparison.Ordinal);
        var threat = xaml.IndexOf("InfoSheetLabelThreat", StringComparison.Ordinal);
        var sec = xaml.IndexOf("InfoSheetLabelSec", StringComparison.Ordinal);
        var general = xaml.IndexOf("InfoSheetLabelGeneral", StringComparison.Ordinal);
        var recent = xaml.IndexOf("InfoSheetLabelRecent", StringComparison.Ordinal);
        var table = xaml.IndexOf("InfoSheetTableKills", StringComparison.Ordinal);

        Assert.True(birthday >= 0 && birthday < threat);
        Assert.True(threat < sec);
        Assert.True(sec < general);
        Assert.True(general < recent);
        Assert.True(recent < table);
    }

    [Fact]
    public void Layout_BindsTheSixTableValues()
    {
        var xaml = File.ReadAllText(FindInfoSheetXaml());

        foreach (var name in new[] { "WeekKillsValue", "WeekSolosValue", "WeekLossesValue", "TotalKillsValue", "TotalSolosValue", "TotalLossesValue" })
            Assert.Contains("{Binding " + name + ",", xaml);
    }

    [Fact]
    public void Faction_EnlistedPilotWithReferenceName_ShowsFullName()
    {
        var row = CreateRow();
        row.FactionId = 500004;

        var viewModel = CreateFactionViewModel(row, _ => "Gallente Federation");

        Assert.Equal(Visibility.Visible, viewModel.FactionVisibility);
        Assert.Equal("Gallente Federation", viewModel.FactionValue);
        Assert.Equal("Faction: ", UiText.InfoSheetLabelFaction);
    }

    [Fact]
    public void Faction_NotEnlisted_IsHidden()
    {
        var viewModel = CreateFactionViewModel(CreateRow(), _ => "Gallente Federation");

        Assert.Equal(Visibility.Collapsed, viewModel.FactionVisibility);
    }

    [Fact]
    public void Faction_NameMissingFromReferenceData_ShowsShortCode()
    {
        var row = CreateRow();
        row.FactionId = 500010;

        var viewModel = CreateFactionViewModel(row, _ => null);

        Assert.Equal(Visibility.Visible, viewModel.FactionVisibility);
        Assert.Equal("Gu", viewModel.FactionValue);
    }

    [Fact]
    public void Faction_NameLookupThrows_ShowsShortCode()
    {
        var row = CreateRow();
        row.FactionId = 500011;

        var viewModel = CreateFactionViewModel(row, _ => throw new InvalidOperationException("SDE table missing"));

        Assert.Equal("An", viewModel.FactionValue);
    }

    [Fact]
    public void Layout_FactionLineFollowsTheTable()
    {
        var xaml = File.ReadAllText(FindInfoSheetXaml());

        var table = xaml.IndexOf("InfoSheetTableTotal", StringComparison.Ordinal);
        var faction = xaml.IndexOf("InfoSheetLabelFaction", StringComparison.Ordinal);

        Assert.True(table >= 0 && table < faction);
        Assert.Contains("{Binding FactionVisibility}", xaml);
        Assert.Contains("{Binding FactionValue,", xaml);
    }

    private static InfoSheetViewModel CreateFactionViewModel(PilotReportRow row, Func<long, string?> lookupFactionName)
    {
        return new InfoSheetViewModel(
            row,
            false,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            _ => Task.FromResult<PilotLastActivitySummary?>(null),
            lookupFactionName);
    }

    private static string FindInfoSheetXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Killright.UI", "InfoSheet", "InfoSheetWindow.xaml");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("InfoSheetWindow.xaml");
    }

    [Fact]
    public void UiText_InfoSheetLoading_IsLoadingWithEllipsis()
    {
        Assert.Equal("Loading…", UiText.InfoSheetLoading);
    }
}
