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
            Kills = "4",
            Solos = "2",
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
        Assert.Equal("4", viewModel.KillsValue);
    }

    [Fact]
    public async Task LoadAsync_UsesInputNameAndCharacterId_AndShowsBirthdayAndLastActivity()
    {
        string? requestedName = null;
        long? requestedId = null;

        var viewModel = new InfoSheetViewModel(
            CreateRow(),
            false,
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
            _ => Task.FromResult<DateOnly?>(new DateOnly(2015, 6, 12)),
            _ => Task.FromResult<PilotLastActivitySummary?>(KillSummary()));

        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.LoadAsync();

        Assert.Contains(nameof(InfoSheetViewModel.BirthdayValue), changed);
        Assert.Contains(nameof(InfoSheetViewModel.LastActivityShipValue), changed);
        Assert.Contains(nameof(InfoSheetViewModel.LastActivityKillOnlyVisibility), changed);
    }

    [Fact]
    public void UiText_InfoSheetLoading_IsLoadingWithEllipsis()
    {
        Assert.Equal("Loading…", UiText.InfoSheetLoading);
    }
}
