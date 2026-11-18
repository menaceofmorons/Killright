using System.Text.Json;
using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Shared;
using Killright.Shared.Killmails;
using Killright.Storage.Database;
using Killright.Storage.Engine;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.Storage.Sde;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.Analysis;

[Collection("RealEngine")]
public sealed class EngineRuntimeWorkStreamIntegrationTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;

    [Fact]
    public void ShippedSettingsFile_LoadsInTheApplicationAndInitialisesTheRealEngine()
    {
        var settingsPath = IntegrationPaths.ShippedSettingsPath();

        Assert.True(ApplicationSettingsLoader.Load(settingsPath).Succeeded);

        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), settingsPath);

        Assert.True(runtime.IsAvailable);
        Assert.Null(runtime.InitializeFailureReason);
        Assert.False(runtime.InitializeFailedOnSettings);
    }

    [Fact]
    public void MissingSettingsFile_IsRefusedByBothTheApplicationLoaderAndTheRealEngine()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing.{Guid.NewGuid():N}.json");

        var loadResult = ApplicationSettingsLoader.Load(missingPath);
        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), missingPath);

        Assert.False(loadResult.Succeeded);
        Assert.Contains("not found", loadResult.Failure);
        Assert.False(runtime.IsAvailable);
        Assert.True(runtime.InitializeFailedOnSettings);
        Assert.Equal("settings_failure", runtime.InitializeFailureReason);
    }

    [Fact]
    public void UnparseableSettingsFile_IsRefusedByTheRealEngineAsASettingsFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"badsettings.{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");

        try
        {
            using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), path);

            Assert.False(ApplicationSettingsLoader.Load(path).Succeeded);
            Assert.False(runtime.IsAvailable);
            Assert.True(runtime.InitializeFailedOnSettings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SettingsWithRetryCountBelowOne_AreRefusedByTheApplicationLoaderAndNamedInTheFailure()
    {
        var path = IntegrationPaths.WriteSettingsWithRetryCount(0);

        try
        {
            var loadResult = ApplicationSettingsLoader.Load(path);

            Assert.False(loadResult.Succeeded);
            Assert.Contains("network.zkill.retryCount", loadResult.Failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingEngineLibrary_LeavesTheApplicationRunningWithUnknownEngineValuesAndTheFailureInNotes()
    {
        var missingDll = Path.Combine(Path.GetTempPath(), $"absent.{Guid.NewGuid():N}", "killright_engine.dll");
        var logged = new List<string>();

        using var runtime = new KillrightEngineRuntime(
            missingDll,
            IntegrationPaths.ShippedSettingsPath(),
            SystemNativeLibrary.Instance,
            logged.Add);

        Assert.False(runtime.IsAvailable);
        Assert.False(runtime.InitializeFailedOnSettings);
        Assert.Equal("missing_runtime", runtime.InitializeFailureReason);
        Assert.Contains(logged, line => line.Contains("missing_runtime"));

        using var database = new SeededDatabase();
        var results = await new RustRecentStyleClient(runtime, database.Reader).AnalyzePilotsAsync([Lukas, Tral]);

        Assert.All(results, result =>
        {
            Assert.Equal("missing_runtime", result.FailureReason);
            Assert.Equal(StyleClassification.Unknown, result.RecentStyle);
            Assert.Equal("Unk", result.ThreatBand);
        });

        var row = PilotReportRowFactory.FromPilot(
            new Pilot { InputName = "Lukas Naarii", CharacterId = Lukas, VerifyStatus = VerifyStatus.Partial },
            null,
            null,
            results[0].RecentStyle,
            results[0].IsRecentPodder,
            results[0].ThreatBand,
            false,
            false,
            results[0].FailureReason);

        Assert.Equal("Unk", row.Threat);
        Assert.Contains("killright_engine analysis failed (missing_runtime)", row.Notes);
    }

    [Fact]
    public async Task EngineRejectingTheContractVersion_LeavesTheApplicationRunningWithUnknownEngineValues()
    {
        var native = new KillrightEngineRuntimeTests.FakeNativeLibrary { InitializeJson = "{\"success\":true,\"contract_version\":0}" };
        var logged = new List<string>();

        using var runtime = new KillrightEngineRuntime("stale.dll", "settings.json", native, logged.Add);

        Assert.False(runtime.IsAvailable);
        Assert.False(runtime.InitializeFailedOnSettings);
        Assert.Equal("contract_version_mismatch", runtime.InitializeFailureReason);
        Assert.Contains(logged, line => line.Contains("contract_version_mismatch"));

        using var database = new SeededDatabase();
        var results = await new RustRecentStyleClient(runtime, database.Reader).AnalyzePilotsAsync([Lukas]);

        Assert.Equal("missing_runtime", results[0].FailureReason);
    }

    [Fact]
    public async Task DisposingTheRealEngineWhileScansAreInFlight_WaitsForThemAndLaterCallsGetTheFallback()
    {
        var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), IntegrationPaths.ShippedSettingsPath());
        Assert.True(runtime.IsAvailable);

        var inFlight = Enumerable.Range(0, 16)
            .Select(_ => runtime.AnalyzePilotsAsync("{\"character_ids\":[],\"pilots\":[]}"))
            .ToArray();

        await Task.Run(runtime.Dispose);

        var responses = await Task.WhenAll(inFlight).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(16, responses.Length);
        Assert.All(responses, response =>
        {
            using var document = JsonDocument.Parse(response);

            Assert.Equal(0, document.RootElement.GetProperty("results").GetArrayLength());
            Assert.False(document.RootElement.TryGetProperty("failure", out _), response);
        });

        var afterDispose = await runtime.AnalyzePilotsAsync("{}");
        Assert.Contains("missing_runtime", afterDispose);
    }

    private sealed class SeededDatabase : IDisposable
    {
        private readonly KillRightDatabase _database;
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"engineWs.{Guid.NewGuid():N}.db");

        public SeededDatabase()
        {
            _database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = _path });
            _database.EnsureCreated();
            _database.Open();

            var now = DateTimeOffset.UtcNow;
            var batch = new ScanWriteBatch();

            foreach (var id in new[] { Lukas, Tral })
            {
                batch.AddIdentity(new PilotIdentityCacheRecord
                {
                    InputName = $"PILOT {id}",
                    CharacterId = id,
                    CharacterName = $"Pilot {id}",
                    VerifyStatus = VerifyStatus.Partial,
                    SecurityStatus = 1.5,
                    CorporationId = 98000001,
                    SecurityStatusAtUtc = now.UtcDateTime,
                    CachedAtUtc = now.UtcDateTime
                });
            }

            batch.AddKillmails(0, Lukas,
            [
                new RawKillmail(
                    910001, "hash1", now.AddDays(-1), 30000142, null, 999, 587, false, false,
                    [new KillmailAttacker(Lukas, 98000001, null, 11567), new KillmailAttacker(Tral, 98000002, null, 17738)])
            ]);

            using var session = _database.OpenScanSession();
            ScanWriter.Commit(session, batch, 11, now);

            Reader = new EngineInputReader(_database, new SdeReferenceDataStore(_database));
        }

        public IEngineInputReader Reader { get; }

        public void Dispose()
        {
            _database.Close();
            File.Delete(_path);
        }
    }
}
