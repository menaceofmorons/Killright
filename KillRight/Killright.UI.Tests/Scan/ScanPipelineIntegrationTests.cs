using System.Net;
using System.Text;
using Killright.Core.Models;
using Killright.Integration.Esi;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Engine;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.Storage.Sde;
using Killright.Storage.zKill;
using Killright.UI.ClipboardMonitoring;
using Killright.UI.Scan;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests.Scan;

[Collection(ApplicationClockCollection.Name)]
public sealed class ScanPipelineIntegrationTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;
    private const long Ignored = 2112625428;
    private const long IgnoredCorporation = 98000009;
    private const string LongName = "Abcdefghijklmnopqrstuvwxyz Abcdefghij";
    private const long LongNameId = 90000077;

    [Fact]
    public async Task RecentCall_InvalidIdAnswer_MarkerIsReadByTheCacheAndTheEngineInputAndTheNextScanSkipsTheCall()
    {
        var database = CreateDatabase();
        var handler = new RouteHandler()
            .On("api/stats/characterID/", """{"error":"Invalid type or id"}""")
            .On("pastSeconds/", """{"error":"Invalid type or id"}""");
        var client = new zKillClient(new HttpClient(handler));
        var now = ApplicationClock.UtcNow;

        var statisticsResult = await client.GetStatisticsAsync(Lukas);
        Assert.Equal(zKillStatisticsOutcome.NoHistory, statisticsResult.Outcome);

        var first = new ScanWriteBatch();
        first.AddStatistics(new PendingStatistics(Lukas, new zKillStatistics { NoHistory = true }, "Unk", true, now));

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, first, 11, now);

        var cached = await ReadCachedStatistics(database);
        Assert.True(cached[Lukas].NoHistory);

        var requestsBefore = handler.Requests.Count;
        var second = new ScanWriteBatch();
        var outcome = await RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, cached[Lukas], false, second, now, null, CancellationToken.None);

        Assert.Equal(requestsBefore, handler.Requests.Count);
        Assert.False(outcome.Failed);
        Assert.Equal(now, outcome.NewLastSuccessfulCallUtc);

        var engineInput = Assert.Single(await new EngineInputReader(database, new SdeReferenceDataStore(database)).ReadPilotInputsAsync([Lukas]));
        Assert.True(engineInput.Inputs!.Statistics!.NoHistoryMarker);
    }

    [Fact]
    public async Task RecentCall_InvalidIdAnswerForAPilotWithStatistics_WritesTheMarkerOnThePresentRowBothReadsAgreeOn()
    {
        var database = CreateDatabase();
        var now = ApplicationClock.UtcNow;
        await new zKillStatisticsCache(database).UpsertAsync(Lukas, Statistics(), "gang", false);
        var handler = new RouteHandler().On("pastSeconds/", """{"error":"Invalid type or id"}""");
        var client = new zKillClient(new HttpClient(handler));
        var cached = await ReadCachedStatistics(database);
        var writes = new ScanWriteBatch();

        var outcome = await RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, cached[Lukas], false, writes, now, null, CancellationToken.None);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, writes, 11, now);

        Assert.False(outcome.Failed);
        Assert.Equal([Lukas], writes.NoHistoryMarks);
        Assert.True((await ReadCachedStatistics(database))[Lukas].NoHistory);

        var engineInput = Assert.Single(await new EngineInputReader(database, new SdeReferenceDataStore(database)).ReadPilotInputsAsync([Lukas]));
        Assert.True(engineInput.Inputs!.Statistics!.NoHistoryMarker);
        Assert.Equal("gang", engineInput.Inputs.Statistics.GeneralStyle);
    }

    [Fact]
    public async Task RecentCall_PagedPeriod_EveryPageIsCountedAndAllKillmailsReachTheScanWrite()
    {
        var database = CreateDatabase();
        var now = ApplicationClock.UtcNow;
        var handler = new RouteHandler()
            .On("pastSeconds/604800/page/3/", "[]")
            .On("pastSeconds/604800/page/2/", KillmailPage(3, 4))
            .On("pastSeconds/604800/", KillmailPage(1, 2));
        var limiter = new CountingLimiter();
        var client = new zKillClient(new HttpClient(handler), limiter: limiter);
        var writes = new ScanWriteBatch();

        var outcome = await RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, StatisticsWithCurrentMonth(now), true, writes, now, null, CancellationToken.None);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, writes, 11, now);

        Assert.False(outcome.Failed);
        Assert.Equal(604800, outcome.PastSecondsRequested);
        Assert.Equal(now, outcome.NewLastSuccessfulCallUtc);
        Assert.Equal(3, limiter.Calls);
        Assert.Equal([1L, 2L, 3L, 4L], Assert.Single(writes.Killmails).Killmails.Select(k => k.KillmailId).ToArray());
        Assert.Equal(4, CountRows(database, "zkill_killmails"));
    }

    [Fact]
    public async Task RecentCall_PageFailure_RecordsNoSuccessAndWritesNothing()
    {
        var database = CreateDatabase();
        var now = ApplicationClock.UtcNow;
        var handler = new RouteHandler()
            .On("pastSeconds/604800/page/2/", "oops", HttpStatusCode.InternalServerError)
            .On("pastSeconds/604800/", KillmailPage(1, 2));
        var client = new zKillClient(new HttpClient(handler));
        var writes = new ScanWriteBatch();

        var outcome = await RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, StatisticsWithCurrentMonth(now), true, writes, now, null, CancellationToken.None);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, writes, 11, now);

        Assert.True(outcome.Failed);
        Assert.Null(outcome.NewLastSuccessfulCallUtc);
        Assert.Null(outcome.PastSecondsRequested);
        Assert.True(writes.IsEmpty);
        Assert.Equal(0, CountRows(database, "zkill_killmails"));
    }

    [Fact]
    public async Task RecentCall_CancelledScanMidPaging_PropagatesCancellationAndLeavesNothingToCommit()
    {
        var now = ApplicationClock.UtcNow;
        var client = new zKillClient(new HttpClient(new HangingHandler()));
        var writes = new ScanWriteBatch();
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, StatisticsWithCurrentMonth(now), true, writes, now, null, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(writes.IsEmpty);
    }

    [Fact]
    public async Task Scan_ListWithLongNameAndIgnoredCorporation_DropsTheIgnoredPilotBeforeAnyZKillCallEvenWhenEntityNamesFail()
    {
        var database = CreateDatabase();
        var now = ApplicationClock.UtcNow;
        var esi = new SeededEsiClient();
        esi.Add("Lukas Naarii", Lukas, 98000001);
        esi.Add("T'ral Vsengne", Tral, 98000002);
        esi.Add(LongName, LongNameId, 98000003);
        esi.Add("Ignored Pilot", Ignored, IgnoredCorporation);
        var resolver = new PilotIdentityResolver(esi, new PilotIdentityCache(database), new EsiEntityNameCache(database), 8);
        var clipboardText = string.Join("\r\n", "Lukas Naarii", "T'ral Vsengne", LongName, "Ignored Pilot", "not a name: !!", "lukas naarii");

        var names = PilotListParser.ParseIfLikelyPilotList(clipboardText);
        var resolved = await resolver.ResolveAsync(names);
        var ignoreList = new[] { new IgnoreListEntry { Id = IgnoredCorporation, Type = IgnoreEntryType.Corporation, Name = "Ignored Corp" } };
        var selected = ScanPilotSelector.SelectForAnalysis(resolved, ignoreList);

        var handler = new RouteHandler()
            .On("api/stats/characterID/", StatisticsJson(now))
            .On("pastSeconds/604800/page/2/", "[]")
            .On("pastSeconds/604800/", KillmailPage(1));
        var client = new zKillClient(new HttpClient(handler));
        var writes = new ScanWriteBatch();

        foreach (var pilot in selected)
        {
            var characterId = pilot.CharacterId!.Value;
            var statistics = await client.GetStatisticsAsync(characterId);
            Assert.Equal(zKillStatisticsOutcome.Success, statistics.Outcome);
            writes.AddStatistics(new PendingStatistics(characterId, statistics.Statistics!, "gang", false, now));

            var outcome = await RecentKillmailRefresher.RefreshAsync(
                client, characterId, 0, null, statistics.Statistics, true, writes, now, null, CancellationToken.None);
            Assert.False(outcome.Failed);
        }

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, writes, 11, now);

        Assert.Equal(4, names.Count);
        Assert.Equal(37, LongName.Length);
        Assert.Equal([Lukas, Tral, LongNameId], selected.Select(pilot => pilot.CharacterId!.Value).ToArray());
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains(Ignored.ToString(), StringComparison.Ordinal));

        var inputs = await new EngineInputReader(database, new SdeReferenceDataStore(database))
            .ReadPilotInputsAsync([Lukas, Tral, LongNameId]);
        Assert.All(inputs, input => Assert.NotNull(input.Inputs!.Statistics));
        Assert.All(inputs, input => Assert.Null(input.FailureReason));
    }

    [Fact]
    public void AppWiring_OneUserAgentSourceDecompressingSdeClientFailureLogAndApplicationClockResolver()
    {
        var source = ReadApp();

        Assert.DoesNotContain("UserAgent", source);
        Assert.Contains("SdeHttpClientFactory.Create()", source);
        Assert.Matches(@"new SdeClient\([^;]*EngineFailureLog\.Record\)", source);
        Assert.Matches(@"new EsiClient\([^;]*log: EngineFailureLog\.Record\)", source);
        Assert.Matches(@"new PilotIdentityResolver\([^;]*\(\) => ApplicationClock\.UtcNow\.UtcDateTime\)", source);
        Assert.Matches(@"AutomaticDecompression\s*=\s*DecompressionMethods\.GZip", source);
        Assert.Matches(@"new zKillClient\([^;]*EngineFailureLog\.Record\)", source);
    }

    private static string StatisticsJson(DateTimeOffset now)
    {
        var month = $"\"year\":{now.Year},\"month\":{now.Month},\"shipsLost\":1,\"shipsDestroyed\":5";

        return "{\"shipsDestroyed\":120,\"soloKills\":30,\"soloRatio\":0.25,\"avgGangSize\":4.0,\"shipsLost\":12,\"soloLosses\":2,"
            + "\"months\":{\"current\":{" + month + "}}}";
    }

    private static zKillStatistics StatisticsWithCurrentMonth(DateTimeOffset now)
    {
        var statistics = Statistics();
        statistics.months = new Dictionary<string, zKillStatisticsMonth>
        {
            ["current"] = new() { Year = now.Year, Month = now.Month, ShipsDestroyed = 5, ShipsLost = 1 }
        };

        return statistics;
    }

    private static zKillStatistics Statistics()
    {
        return new zKillStatistics
        {
            shipsDestroyed = 120,
            soloKills = 30,
            soloRatio = 0.25,
            avgGangSize = 4.0,
            shipsLost = 12,
            soloLosses = 2,
            podKills = 1
        };
    }

    private static async Task<IReadOnlyDictionary<long, zKillStatistics>> ReadCachedStatistics(KillRightDatabase database)
    {
        return await new zKillStatisticsCache(database).GetManyAsync([Lukas, Tral, LongNameId], TimeSpan.FromDays(30));
    }

    private static string KillmailPage(params long[] ids)
    {
        var items = ids.Select(id => $$$"""
            {"killmail_id":{{{id}}},"killmail_time":"2026-09-20T10:00:00Z","solar_system_id":30000142,
             "victim":{"character_id":999,"ship_type_id":587},"attackers":[],
             "zkb":{"hash":"h{{{id}}}","locationID":40000001,"solo":true,"npc":false}}
            """);

        return "[" + string.Join(",", items) + "]";
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scanIntegration.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return database;
    }

    private static int CountRows(KillRightDatabase database, string table)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{table};";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string ReadApp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", "App.xaml.cs")).Replace("\r\n", "\n");
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body, HttpStatusCode Status)> _routes = [];
        private readonly object _gate = new();
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_gate)
                    return _requests.ToList();
            }
        }

        public RouteHandler On(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((fragment, body, status));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();

            lock (_gate)
                _requests.Add(uri);

            var route = _routes.FirstOrDefault(candidate => uri.Contains(candidate.Fragment, StringComparison.Ordinal));
            var response = route.Fragment is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };

            return Task.FromResult(response);
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CountingLimiter : Killright.Integration.RateLimiting.IRequestStartLimiter
    {
        public int Calls { get; private set; }

        public long TotalWaitMilliseconds => 0;

        public bool IsPaused => false;

        public void Pause(TimeSpan duration)
        {
        }

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class SeededEsiClient : IEsiClient
    {
        private readonly Dictionary<string, (long Id, string Name)> _characters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, long> _corporations = new();

        public long RequestCount => 0;

        public void Add(string name, long id, long corporationId)
        {
            _characters[name] = (id, name);
            _corporations[id] = corporationId;
        }

        public Task<IReadOnlyDictionary<string, EsiNameLookup>> ResolveNamesAsync(IReadOnlyList<string> exactNames, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, EsiNameLookup> result = exactNames.ToDictionary(
                name => name,
                name => _characters.TryGetValue(name, out var character) ? EsiNameLookup.Matched(character.Id, character.Name) : EsiNameLookup.NoMatch,
                StringComparer.OrdinalIgnoreCase);

            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<long, EsiAffiliation>> GetAffiliationsAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<long, EsiAffiliation> result = characterIds
                .Where(_corporations.ContainsKey)
                .ToDictionary(id => id, id => new EsiAffiliation(id, _corporations[id], null));

            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<long, string>> GetEntityNamesAsync(IReadOnlyList<long> entityIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());
        }

        public Task<EsiCharacterDetails?> GetCharacterDetailsAsync(long characterId, CancellationToken cancellationToken = default)
        {
            var name = _characters.Values.First(character => character.Id == characterId).Name;

            return Task.FromResult<EsiCharacterDetails?>(
                new EsiCharacterDetails(characterId, name, _corporations[characterId], null, 0.1, new DateOnly(2018, 1, 1), null));
        }

        public Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
