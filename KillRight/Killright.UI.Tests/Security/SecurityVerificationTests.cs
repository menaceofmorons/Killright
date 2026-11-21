using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Killright.Core.Models;
using Killright.Integration;
using Killright.Integration.Esi;
using Killright.Integration.RateLimiting;
using Killright.Integration.Sde;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Killmails;
using Killright.Shared.Sde;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Engine;
using Killright.Storage.Identity;
using Killright.Storage.Killmails;
using Killright.Storage.Scan;
using Killright.Storage.Sde;
using Killright.UI.Analysis;
using Killright.UI.Tests.Analysis;
using Killright.UI.Configuration;
using Killright.UI.Scan;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.UI.Tests.Security;

[Collection("RealEngine")]
public sealed class SecurityVerificationTests
{
    private const long Lukas = 95465499;
    private const long LukasCorporation = 98765;

    private static readonly string[] AllowedHosts = ["esi.evetech.net", "zkillboard.com", "developers.eveonline.com"];

    private static readonly string[] HostileNames =
    [
        "Robert'); DROP TABLE pilot_identity_cache;--",
        "semi;colon; DELETE FROM pilot_identity_cache",
        "SELECT * FROM sqlite_schema",
        "\"double\" 'single' `back`",
        "O'Brien",
        "100% _like_ \\ escape",
        "-- comment /* block */",
        "UNION SELECT NULL",
        "nul\0inside"
    ];

    [Fact]
    public async Task FullScanPipeline_ContactsOnlyEsiZKillboardAndSdeHosts_WithOneUserAgent_AndLeavesTheAppFolderUnchanged()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), $"securityData.{Guid.NewGuid():N}");
        var appFolderBefore = ListFiles(AppContext.BaseDirectory);
        var handler = new RecordingHandler()
            .On("universe/ids", $$"""{"characters":[{"id":{{Lukas}},"name":"Lukas Naarii"}]}""")
            .On("characters/affiliation", $$"""[{"character_id":{{Lukas}},"corporation_id":{{LukasCorporation}}}]""")
            .On("universe/names", $$"""[{"id":{{LukasCorporation}},"name":"Test Corp"}]""")
            .On($"characters/{Lukas}", """{"name":"Lukas Naarii","corporation_id":98765,"security_status":0.4,"birthday":"2018-01-01T00:00:00Z"}""")
            .On("api/stats/characterID/", """{"shipsDestroyed":120,"soloKills":30,"soloRatio":0.25,"avgGangSize":4.0,"shipsLost":12,"soloLosses":2}""")
            .On("page/2/", "[]")
            .On("pastSeconds/", KillmailPage(1))
            .On("api/characterID/", "[]")
            .On("latest.jsonl", "{\"_key\": \"sde\", \"buildNumber\": 4242}\n");

        try
        {
            var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = Path.Combine(dataFolder, "KillRight.db") });
            database.EnsureCreated();
            var now = ApplicationClock.UtcNow;

            var esi = new EsiClient(new HttpClient(handler));
            var resolver = new PilotIdentityResolver(esi, new PilotIdentityCache(database), new EsiEntityNameCache(database), 8, () => now.UtcDateTime);
            var resolved = await resolver.ResolveAsync(["Lukas Naarii"]);

            var zkill = new zKillClient(new HttpClient(handler));
            var statistics = await zkill.GetStatisticsAsync(Lukas);
            var writes = new ScanWriteBatch();
            writes.AddStatistics(new PendingStatistics(Lukas, statistics.Statistics!, "gang", false, now));
            var recent = await RecentKillmailRefresher.RefreshAsync(zkill, Lukas, 0, null, statistics.Statistics, true, writes, now, null, CancellationToken.None);
            var last = await zkill.GetLastKillmailAsync(Lukas);

            using (var session = database.OpenScanSession())
                ScanWriter.Commit(session, writes, 11, now);

            var sde = await new SdeClient(new HttpClient(handler)).GetManifestAsync();

            var backupFolder = Path.Combine(dataFolder, KillmailBackupDefaults.DefaultBackupFolderName);
            await new DatabaseBackupService(database, backupFolder, 3).BackupAsync();

            Assert.Equal(Lukas, Assert.Single(resolved).CharacterId);
            Assert.False(recent.Failed);
            Assert.Equal(zKillLastKillmailOutcome.Success, last.Outcome);
            Assert.Equal(SdeManifestOutcome.Success, sde.Outcome);

            var hosts = handler.Requests.Select(request => request.Host).Distinct().OrderBy(host => host).ToArray();
            Assert.Equal(AllowedHosts.OrderBy(host => host).ToArray(), hosts);
            Assert.All(handler.Requests, request => Assert.Equal(ClientIdentity.UserAgent, request.UserAgent));
            Assert.DoesNotContain(handler.Requests, request => request.Scheme != Uri.UriSchemeHttps);

            Assert.True(File.Exists(Path.Combine(dataFolder, "KillRight.db")));
            Assert.Single(Directory.EnumerateFiles(backupFolder, "KillRight-*.db"));
            Assert.Equal(appFolderBefore, ListFiles(AppContext.BaseDirectory));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteFolder(dataFolder);
        }
    }

    [Fact]
    public void DiagnosticFilePaths_ResolveUnderThePerUserKillRightFolder()
    {
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillRight");

        Assert.Equal(dataFolder, Path.GetDirectoryName(EngineFailureLogPaths.GetDefaultLogPath()));
        Assert.Equal(dataFolder, Path.GetDirectoryName(ScanTimingLogPaths.GetDefaultLogPath()));
        Assert.StartsWith(ScanTimingLogPaths.FilePrefix, Path.GetFileName(ScanTimingLogPaths.GetDefaultLogPath()));
    }

    [Fact]
    public async Task SettingsFile_IsByteIdenticalAfterStartupLoadEngineInitialisationAndAnalysis()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings.{Guid.NewGuid():N}.json");
        File.Copy(IntegrationPaths.ShippedSettingsPath(), path);
        var bytesBefore = File.ReadAllBytes(path);
        var writtenBefore = File.GetLastWriteTimeUtc(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var dataFolder = Path.Combine(Path.GetTempPath(), $"securitySettings.{Guid.NewGuid():N}");

        try
        {
            var loaded = ApplicationSettingsLoader.Load(path);
            Assert.True(loaded.Succeeded);

            using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), path);
            Assert.True(runtime.IsAvailable);

            var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = Path.Combine(dataFolder, "KillRight.db") });
            database.EnsureCreated();
            database.Open();
            var batch = new ScanWriteBatch();
            batch.AddIdentity(new PilotIdentityCacheRecord
            {
                InputName = $"PILOT {Lukas}",
                CharacterId = Lukas,
                CharacterName = "Pilot",
                VerifyStatus = VerifyStatus.Partial,
                SecurityStatus = 1.5,
                CorporationId = LukasCorporation,
                SecurityStatusAtUtc = ApplicationClock.UtcNow.UtcDateTime,
                CachedAtUtc = ApplicationClock.UtcNow.UtcDateTime
            });

            using (var session = database.OpenScanSession())
                ScanWriter.Commit(session, batch, 11, ApplicationClock.UtcNow);

            var client = new RustRecentStyleClient(runtime, new EngineInputReader(database, new SdeReferenceDataStore(database)), loaded.Settings!.ThreatBands);
            var results = await client.AnalyzePilotsAsync([Lukas]);

            Assert.Single(results);
            Assert.Equal(bytesBefore, File.ReadAllBytes(path));
            Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            DeleteFolder(dataFolder);
        }
    }

    [Fact]
    public async Task HostileNames_RoundTripThroughRealSqliteAsBoundParametersAndLeaveTheSchemaIntact()
    {
        var database = CreateDatabase(out var folder);

        try
        {
            var cache = new PilotIdentityCache(database);
            var nameCache = new EsiEntityNameCache(database);
            var tablesBefore = ReadTableNames(database);

            for (var index = 0; index < HostileNames.Length; index++)
            {
                var name = HostileNames[index];
                await cache.UpsertRecordAsync(new PilotIdentityCacheRecord
                {
                    InputName = PilotIdentityCacheRecord.NormalizeInputName(name),
                    CharacterId = 1000 + index,
                    CharacterName = name,
                    VerifyStatus = VerifyStatus.Verified,
                    CachedAtUtc = ApplicationClock.UtcNow.UtcDateTime
                });
                await nameCache.UpsertAsync([new EsiEntityName(5000 + index, "corporation", name)]);
            }

            for (var index = 0; index < HostileNames.Length; index++)
            {
                var record = await cache.GetRecordAsync(HostileNames[index]);

                Assert.NotNull(record);
                Assert.Equal(HostileNames[index], record!.CharacterName);
                Assert.Equal(1000 + index, record.CharacterId);
                Assert.Equal(HostileNames[index], (await nameCache.GetNamesAsync([5000 + index]))[5000 + index]);
            }

            Assert.Equal(HostileNames.Length, CountRows(database, "pilot_identity_cache"));
            Assert.Equal(HostileNames.Length, CountRows(database, "esi_entity_name_cache"));
            Assert.Equal(tablesBefore, ReadTableNames(database));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task ClipboardNames_ResolvedThroughEsiAndTheCache_ReachStorageOnlyAsBoundParameters()
    {
        var database = CreateDatabase(out var folder);

        try
        {
            var handler = new RecordingHandler().On("universe/ids", "{}");
            var resolver = new PilotIdentityResolver(new EsiClient(new HttpClient(handler)), new PilotIdentityCache(database), new EsiEntityNameCache(database), 4);
            var tablesBefore = ReadTableNames(database);

            var pilots = await resolver.ResolveAsync(HostileNames);

            Assert.Equal(HostileNames.Length, pilots.Count);
            Assert.All(pilots, pilot => Assert.NotEqual(VerifyStatus.Verified, pilot.VerifyStatus));
            Assert.Equal(tablesBefore, ReadTableNames(database));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task NonFiniteValue_IsRejectedAndTheWriteRollsBackLeavingTheEarlierRowIntact(double securityStatus)
    {
        var database = CreateDatabase(out var folder);

        try
        {
            var cache = new PilotIdentityCache(database);
            await cache.UpsertRecordAsync(Record("PILOT ONE", 0.5));

            await Assert.ThrowsAnyAsync<ArgumentException>(() => cache.UpsertRecordAsync(Record("PILOT ONE", securityStatus)));

            var record = await cache.GetRecordAsync("PILOT ONE");
            Assert.Equal(0.5, record!.SecurityStatus);
            Assert.Equal(1, CountRows(database, "pilot_identity_cache"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task Storage_UsesOneConnectionPerReadAndPerWrite_ScopesTransactionsAndRollsBackUncommittedWork()
    {
        var database = CreateDatabase(out var folder);

        try
        {
            var cache = new PilotIdentityCache(database);

            var opened = database.ConnectionsOpened;
            await cache.UpsertRecordAsync(Record("PILOT ONE", 0.5));
            Assert.Equal(opened + 1, database.ConnectionsOpened);

            opened = database.ConnectionsOpened;
            await cache.GetRecordAsync("PILOT ONE");
            await cache.GetRecordAsync("PILOT ONE");
            Assert.Equal(opened + 2, database.ConnectionsOpened);

            using (var scope = database.BeginWrite())
            {
                using var delete = scope.Connection.CreateCommand();
                delete.Transaction = scope.Transaction;
                delete.CommandText = "DELETE FROM pilot_identity_cache;";
                delete.ExecuteNonQuery();
            }

            Assert.Equal(1, CountRows(database, "pilot_identity_cache"));

            using var first = database.OpenConnection();
            using var second = database.OpenConnection();
            Assert.NotSame(first, second);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void EveryTable_IsStrictlyTyped()
    {
        var database = CreateDatabase(out var folder);

        try
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, strict FROM pragma_table_list WHERE schema = 'main' AND type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = command.ExecuteReader();
            var tables = new List<(string Name, long Strict)>();

            while (reader.Read())
                tables.Add((reader.GetString(0), reader.GetInt64(1)));

            Assert.NotEmpty(tables);
            Assert.All(tables, table => Assert.True(table.Strict == 1, $"{table.Name} is not STRICT"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task Times_AreStoredAsUtcInstants_WhateverTheKindOrOffsetWritten()
    {
        var database = CreateDatabase(out var folder);

        try
        {
            var cache = new PilotIdentityCache(database);
            var local = new DateTime(2026, 9, 28, 14, 30, 0, DateTimeKind.Local);

            await cache.UpsertRecordAsync(new PilotIdentityCacheRecord
            {
                InputName = "PILOT ONE",
                VerifyStatus = VerifyStatus.Partial,
                CachedAtUtc = local
            });

            var record = await cache.GetRecordAsync("PILOT ONE");
            Assert.Equal(local.ToUniversalTime(), record!.CachedAtUtc);
            Assert.Equal(DateTimeKind.Utc, record.CachedAtUtc.Kind);

            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT cached_at_utc FROM pilot_identity_cache;";
            Assert.Equal(new DateTimeOffset(local).ToUnixTimeSeconds(), Convert.ToInt64(command.ExecuteScalar()));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public async Task ZKillboardCalls_ShareOneBudget_AndARateLimitRefusalPausesEveryCallWithoutSendingThem()
    {
        var counting = new CountingLimiter();
        var okHandler = new RecordingHandler()
            .On("api/stats/characterID/", """{"shipsDestroyed":1,"soloKills":0,"soloRatio":0,"avgGangSize":2,"shipsLost":0,"soloLosses":0}""")
            .On("api/characterID/", "[]");
        var shared = new zKillClient(new HttpClient(okHandler), limiter: counting);

        await shared.GetStatisticsAsync(Lukas);
        await shared.GetRecentKillmailsAsync(Lukas, 3600);
        await shared.GetLastKillmailAsync(Lukas);

        Assert.Equal(3, counting.Calls);

        var refusal = new RecordingHandler().On("api/", "slow down", HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(30));
        var budget = new RollingWindowRequestBudget(550, TimeSpan.FromSeconds(60));
        var limited = new zKillClient(new HttpClient(refusal), new zKillClientOptions { RateLimitPause = TimeSpan.FromSeconds(60) }, budget);

        var statistics = await limited.GetStatisticsAsync(Lukas);
        var recent = await limited.GetRecentKillmailsAsync(Lukas, 3600);
        var last = await limited.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, statistics.Outcome);
        Assert.Equal(zKillRecentKillmailOutcome.Failure, recent.Outcome);
        Assert.Equal(zKillLastKillmailOutcome.Failure, last.Outcome);
        Assert.True(budget.IsPaused);
        Assert.Single(refusal.Requests);
    }

    [Fact]
    public async Task UserAgent_NamesProductVersionAndProjectUrlWithNoPersonalData_OnEveryRequestOfEveryClient()
    {
        var handler = new RecordingHandler()
            .On("universe/ids", "{}")
            .On("api/", "[]")
            .On("latest.jsonl", "{\"buildNumber\": 1}\n");

        await new EsiClient(new HttpClient(handler)).ResolveNamesAsync(["A"]);
        await new zKillClient(new HttpClient(handler)).GetLastKillmailAsync(Lukas);
        await new SdeClient(new HttpClient(handler)).GetManifestAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(ClientIdentity.UserAgent, request.UserAgent));
        Assert.Matches(@"^KillRight/\d+\.\d+\.\d+ \(\+https://github\.com/[^\s)]+\)$", ClientIdentity.UserAgent);
        Assert.DoesNotContain("@", ClientIdentity.UserAgent);
        Assert.DoesNotContain(Environment.UserName, ClientIdentity.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, ClientIdentity.UserAgent, StringComparison.OrdinalIgnoreCase);
    }

    private static PilotIdentityCacheRecord Record(string inputName, double securityStatus)
    {
        return new PilotIdentityCacheRecord
        {
            InputName = inputName,
            CharacterId = 1,
            CharacterName = inputName,
            VerifyStatus = VerifyStatus.Verified,
            SecurityStatus = securityStatus,
            SecurityStatusAtUtc = ApplicationClock.UtcNow.UtcDateTime,
            CachedAtUtc = ApplicationClock.UtcNow.UtcDateTime
        };
    }

    private static string KillmailPage(long id)
    {
        return $$$"""
            [{"killmail_id":{{{id}}},"killmail_time":"2026-09-20T10:00:00Z","solar_system_id":30000142,
              "victim":{"character_id":999,"ship_type_id":587},"attackers":[],
              "zkb":{"hash":"h{{{id}}}","locationID":40000001,"solo":true,"npc":false}}]
            """;
    }

    private static KillRightDatabase CreateDatabase(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), $"security.{Guid.NewGuid():N}");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = Path.Combine(folder, "KillRight.db") });
        database.EnsureCreated();

        return database;
    }

    private static void Cleanup(string folder)
    {
        SqliteConnection.ClearAllPools();
        DeleteFolder(folder);
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static int CountRows(KillRightDatabase database, string table)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{table};";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string[] ReadTableNames(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();

        while (reader.Read())
            names.Add(reader.GetString(0));

        return names.ToArray();
    }

    private static string[] ListFiles(string folder)
    {
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => path + "|" + new FileInfo(path).Length)
            .Order()
            .ToArray();
    }

    private sealed record RecordedRequest(string Scheme, string Host, string Uri, string UserAgent);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body, HttpStatusCode Status, TimeSpan? RetryAfter)> _routes = [];
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                    return _requests.ToList();
            }
        }

        public RecordingHandler On(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, TimeSpan? retryAfter = null)
        {
            _routes.Add((fragment, body, status, retryAfter));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;

            lock (_gate)
                _requests.Add(new RecordedRequest(uri.Scheme, uri.Host, uri.ToString(), request.Headers.UserAgent.ToString()));

            var route = _routes.FirstOrDefault(candidate => uri.ToString().Contains(candidate.Fragment, StringComparison.Ordinal));

            if (route.Fragment is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var response = new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };

            if (route.RetryAfter is { } delay)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);

            return Task.FromResult(response);
        }
    }

    private sealed class CountingLimiter : IRequestStartLimiter
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
}
