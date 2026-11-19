using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.Esi;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Time;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Killright.UI.InfoSheet;
using Killright.UI.Resources;
using Killright.UI.Scan;
using Killright.UI.Tests.Scan;
using Killright.UI.UiState;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests;

[Collection(ApplicationClockCollection.Name)]
public sealed class WorkStream16IntegrationTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static Pilot ResolvedPilot(string name, long id, long? factionId = null) => new()
    {
        InputName = name,
        CharacterId = id,
        CharacterName = name,
        VerifyStatus = VerifyStatus.Partial,
        CorporationId = 10,
        Corporation = new Corporation { CorporationId = 10, Name = "Corp" },
        FactionId = factionId
    };

    private static PilotReportRow Row(
        Pilot pilot,
        string threat,
        zKillActivity? activity = null,
        bool statisticsFailed = false,
        bool recentFailed = false,
        string? engineFailure = null) =>
        PilotReportRowFactory.FromPilot(pilot, activity, null, StyleClassification.Solo, false, threat, statisticsFailed, recentFailed, engineFailure);

    private static zKillActivity ActivityWithLastKill(TimeSpan age) =>
        new(1, true, 1, 0, null, null, DateTimeOffset.UtcNow, LastKillUtc: ApplicationClock.UtcNow - age);

    private static IReadOnlyList<string> SortMemberPathsInMainWindowXaml()
    {
        var xaml = File.ReadAllText(Path.Combine(FindUiProjectDirectory(), "MainWindow.xaml"));
        return Regex.Matches(xaml, "SortMemberPath=\"(?<path>[^\"]+)\"").Select(match => match.Groups["path"].Value).ToList();
    }

    private static string FindUiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        return Path.Combine(directory!.FullName, "Killright.UI");
    }

    private static List<string> SortedPilots(IEnumerable<PilotReportRow> rows, string path, ListSortDirection direction = ListSortDirection.Ascending)
    {
        var view = new ListCollectionView(rows.ToList());
        view.SortDescriptions.Add(new SortDescription(path, direction));
        return view.Cast<PilotReportRow>().Select(row => row.Pilot).ToList();
    }

    [Fact]
    public void MainWindowXaml_SortMemberPaths_AreSortKeyPropertiesOfTheRow()
    {
        var paths = SortMemberPathsInMainWindowXaml();

        Assert.Equal(["ThreatSortKey", "SecuritySortKey", "WeekSortKey", "LastKillSortKey"], paths);

        foreach (var path in paths)
            Assert.NotNull(typeof(PilotReportRow).GetProperty(path));
    }

    [Fact]
    public void Grid_SortsThreatBySeverity_AndLastKillTenDaysAfterOneHour()
    {
        var rows = new[]
        {
            Row(ResolvedPilot("Extreme", 1), "5", ActivityWithLastKill(TimeSpan.FromDays(10))),
            Row(ResolvedPilot("Low", 2), "1", ActivityWithLastKill(TimeSpan.FromMinutes(30))),
            Row(ResolvedPilot("Nothing", 3), "None"),
            Row(ResolvedPilot("High", 4), "3", ActivityWithLastKill(TimeSpan.FromDays(40))),
            Row(ResolvedPilot("Unknown", 5), "Unk")
        };

        Assert.Equal(["Unknown", "Nothing", "Low", "High", "Extreme"], SortedPilots(rows, "ThreatSortKey"));
        var lastKill = SortedPilots(rows, "LastKillSortKey");
        Assert.True(lastKill.IndexOf("Low") < lastKill.IndexOf("Extreme"));
        Assert.True(lastKill.IndexOf("Extreme") < lastKill.IndexOf("High"));
        Assert.Equal("1h", rows[1].LastKill);
        Assert.Equal("10d", rows[0].LastKill);
        Assert.Equal(">30d", rows[3].LastKill);
    }

    [Fact]
    public void GridThreatKey_AndInfoSheet_ShareOneResourceSetBandName()
    {
        var expected = new Dictionary<string, string>
        {
            ["None"] = UiText.ThreatBandNone,
            ["1"] = UiText.ThreatBandLow,
            ["2"] = UiText.ThreatBandMedium,
            ["3"] = UiText.ThreatBandHigh,
            ["4"] = UiText.ThreatBandVeryHigh,
            ["5"] = UiText.ThreatBandExtreme,
            ["Unk"] = UiText.ThreatBandUnk
        };

        foreach (var (key, word) in expected)
        {
            var row = Row(ResolvedPilot("P", 1), key);
            var sheet = NewSheet(row);

            Assert.Equal(key, row.Threat);
            Assert.False(key.All(char.IsDigit) && key == word);
            Assert.Equal(word, sheet.ThreatValue);
        }
    }

    [Fact]
    public void FailedCalls_GridShowsItalicPilotAndNotes_AndInfoSheetNamesTheSourceFirst()
    {
        var row = Row(ResolvedPilot("Failed Pilot", 1), "Unk", statisticsFailed: true, recentFailed: true);

        Assert.True(row.DataRetrievalFailed);
        Assert.Equal("zKillboard statistics, zKillboard recent killmails", row.Notes);
        Assert.NotNull(row.StatsFailureSource);

        var sheet = NewSheet(row, developerModeRevealed: true);

        Assert.Equal(2, sheet.TitleLines.Count);
        Assert.True(sheet.TitleLines[0].IsError);
        Assert.Equal(UiText.FormatErrorLoading(row.StatsFailureSource!), sheet.TitleLines[0].Text);
        Assert.False(sheet.TitleLines[1].IsError);
        Assert.Equal(UiText.FormatStatsTitle("Failed Pilot"), sheet.TitleLines[1].Text);
        Assert.Equal(row.Notes, sheet.NotesValue);
    }

    [Fact]
    public async Task PilotWithoutKillmail_InfoSheetLastActivityBodyIsEmpty_AndALoadFailureKeepsThePlaceholders()
    {
        var row = Row(ResolvedPilot("Quiet Pilot", 1), "1");
        row.StatsFailureSource = null;

        var empty = NewSheet(row, loadLastActivity: _ => Task.FromResult<PilotLastActivitySummary?>(null));
        await empty.LoadAsync();

        Assert.Equal(Visibility.Collapsed, empty.LastActivityBodyVisibility);
        Assert.Equal(string.Empty, empty.LastActivityDateTimeValue);
        Assert.Equal(Visibility.Collapsed, empty.ErrorLineVisibility);

        var failed = NewSheet(row, loadLastActivity: _ => throw new InvalidOperationException("lookup failed"));
        await failed.LoadAsync();

        Assert.Equal(Visibility.Visible, failed.ErrorLineVisibility);
        Assert.StartsWith(UiText.FormatErrorLoading(string.Empty).TrimEnd(), failed.ErrorLine);
        Assert.True(failed.TitleLines[0].IsError);
        Assert.Equal(UiText.PlaceholderDash, failed.LastActivityDateTimeValue);
    }

    [Fact]
    public void AngelsFaction_ShowsTheAgAbbreviationOnGridAndInfoSheet()
    {
        var row = Row(ResolvedPilot("Angel", 1, factionId: 500011), "1");
        var sheet = NewSheet(row);

        Assert.Contains("Ag", row.FactionWarfare);
        Assert.DoesNotContain("An", row.FactionWarfare);
        Assert.Equal("Ag", sheet.FactionValue);
        Assert.Equal(Visibility.Visible, sheet.FactionVisibility);
    }

    [Fact]
    public void MenuModalPreview_RevealingTheDeveloperTab_ThenCancel_LeavesTheStateFileUnchanged_AndOkStoresIt()
    {
        Sta.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var store = new UiStateStore(path);
                var gate = new ColumnLayoutPersistenceGate();
                var notes = store.Current.Columns.Single(column => column.Id == ColumnIds.Notes);

                Assert.False(store.Current.DeveloperTabRevealed);
                Assert.False(UiStateDefaults.IsEffectivelyVisible(notes, store.Current.DeveloperTabRevealed));

                var working = store.Current with
                {
                    DeveloperTabRevealed = true,
                    Columns = store.Current.Columns.Select(column => column.Id == ColumnIds.Notes ? column with { Visible = true } : column).ToList()
                };

                using (gate.BeginPreview())
                {
                    Assert.True(UiStateDefaults.IsEffectivelyVisible(working.Columns.Single(column => column.Id == ColumnIds.Notes), working.DeveloperTabRevealed));

                    if (gate.ShouldPersist)
                        store.UpdateColumns(working.Columns);
                }

                Assert.False(store.Current.DeveloperTabRevealed);
                Assert.False(File.Exists(path));

                store.Commit(working);

                var reloaded = new UiStateStore(path).Current;
                Assert.True(reloaded.DeveloperTabRevealed);
                Assert.True(UiStateDefaults.IsEffectivelyVisible(reloaded.Columns.Single(column => column.Id == ColumnIds.Notes), reloaded.DeveloperTabRevealed));
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public async Task NewAcceptedList_CancelsARunningScanAtTheIdentityResolver()
    {
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = Path.Combine(Path.GetTempPath(), $"ws16.{Guid.NewGuid():N}.db") });
        database.EnsureCreated();

        var esi = new BlockingEsiClient();
        var resolver = new PilotIdentityResolver(esi, new PilotIdentityCache(database), new EsiEntityNameCache(database), 4);
        var firstCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var coordinator = new ScanCoordinator(
            async (names, _, context) =>
            {
                if (names[0] != "First Pilot")
                    return;

                try
                {
                    await resolver.ResolveAsync(names, null, context.Token);
                    firstCancelled.TrySetResult(false);
                }
                catch (OperationCanceledException)
                {
                    firstCancelled.TrySetResult(true);
                    throw;
                }
            },
            () => { },
            () => { });

        var first = coordinator.Submit(["First Pilot"]);
        await esi.Entered.Task.WaitAsync(WaitLimit);
        var second = coordinator.Submit(["Second Pilot"]);

        Assert.True(await firstCancelled.Task.WaitAsync(WaitLimit));
        await Task.WhenAll(first, second).WaitAsync(WaitLimit);
        Assert.Equal(1, esi.NameCalls);
    }

    private static InfoSheetViewModel NewSheet(
        PilotReportRow row,
        bool developerModeRevealed = false,
        Func<long, Task<PilotLastActivitySummary?>>? loadLastActivity = null) =>
        new(
            row,
            developerModeRevealed,
            _ => Task.FromResult<InfoSheetTotals?>(null),
            _ => Task.FromResult<DateOnly?>(null),
            loadLastActivity ?? (_ => Task.FromResult<PilotLastActivitySummary?>(null)));

    private sealed class BlockingEsiClient : IEsiClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int NameCalls { get; private set; }

        public long RequestCount => 0;

        public async Task<IReadOnlyDictionary<string, EsiNameLookup>> ResolveNamesAsync(IReadOnlyList<string> exactNames, CancellationToken cancellationToken = default)
        {
            NameCalls++;
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new Dictionary<string, EsiNameLookup>();
        }

        public Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<long, EsiAffiliation>> GetAffiliationsAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<long, string>> GetEntityNamesAsync(IReadOnlyList<long> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EsiCharacterDetails?> GetCharacterDetailsAsync(long characterId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
