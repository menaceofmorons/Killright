using System.Text.Json.Nodes;
using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Shared;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Engine;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.Storage.Sde;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Analysis;

[Collection("RealEngine")]
public sealed class ScanWiringIntegrationTests
{
    private const long PilotA = 95465499;
    private const long PilotB = 91321792;
    private const long PilotC = 92000003;
    private const long PilotD = 92000004;
    private const long CapsuleTypeId = 670;

    [Fact]
    public void ShippedSettingsFile_CarriesTheKeysAddedByTheWorkStreamWithTheRecordedValues()
    {
        var root = JsonNode.Parse(File.ReadAllText(IntegrationPaths.ShippedSettingsPath()))!;

        Assert.Equal(75, (int)root["groupDetection"]!["chainStrengthMaximum"]!);
        Assert.Equal(40, (int)root["threat"]!["gating"]!["killLossCap1"]!);
        Assert.Equal(60, (int)root["style"]!["recentSoloSharePercent"]!);
        Assert.True(ApplicationSettingsLoader.Load(IntegrationPaths.ShippedSettingsPath()).Succeeded);
    }

    [Theory]
    [InlineData("groupDetection", "chainStrengthMaximum")]
    [InlineData("threat.gating", "killLossCap1")]
    [InlineData("style", "recentSoloSharePercent")]
    public void SettingsFileMissingAKeyAddedByTheWorkStream_IsRefusedByTheRealEngineAsASettingsFailure(string section, string key)
    {
        var path = WriteSettings(root => Section(root, section).AsObject().Remove(key));

        try
        {
            using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), path);

            Assert.False(runtime.IsAvailable);
            Assert.True(runtime.InitializeFailedOnSettings);
            Assert.Equal("settings_failure", runtime.InitializeFailureReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("solo", "4")]
    [InlineData("solo_beginner", "3")]
    [InlineData("gang", "4")]
    [InlineData("gang_beginner", "3")]
    public async Task GeneralStyleCodeStoredByTheApplication_DrivesTheStyleModifierInTheRealEngineThreat(string code, string expectedBand)
    {
        Assert.True(StyleCodes.TryFromCode(code, out _));

        using var database = new ScanDatabase();
        database.SeedPilot(PilotA, Statistics(shipsDestroyed: 1000, soloKills: 150, shipsLost: 100, soloLosses: 50), code);
        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), IntegrationPaths.ShippedSettingsPath());

        var result = (await database.Client(runtime).AnalyzePilotsAsync([PilotA])).Single();

        Assert.Null(result.FailureReason);
        Assert.Equal(expectedBand, result.ThreatBand);
    }

    [Fact]
    public void RealEngine_IsAcceptedByTheApplicationAtContractVersionTwo()
    {
        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), IntegrationPaths.ShippedSettingsPath());

        Assert.Equal(2, KillrightEngineRuntime.ExpectedContractVersion);
        Assert.True(runtime.IsAvailable);
        Assert.Null(runtime.InitializeFailureReason);
    }

    [Fact]
    public async Task PodKillsInTheStoredStatistics_AreRemovedBeforeTheRealEngineScoresThreat()
    {
        var withoutPods = Statistics(shipsDestroyed: 1000, soloKills: 150, shipsLost: 100, soloLosses: 50);
        var withPods = Statistics(shipsDestroyed: 1000, soloKills: 150, shipsLost: 100, soloLosses: 50);
        withPods.podKills = 900;

        Assert.Equal("4", await ThreatBandFor(IntegrationPaths.ShippedSettingsPath(), withoutPods));
        Assert.Equal("2", await ThreatBandFor(IntegrationPaths.ShippedSettingsPath(), withPods));
    }

    [Fact]
    public async Task KillLossCapOneInTheSettingsFile_ControlsTheRealEngineScoreIndependentlyOfTheMediumCap()
    {
        var statistics = Statistics(shipsDestroyed: 4000, soloKills: 600, shipsLost: 10000, soloLosses: 0, soloRatio: 70);

        var shipped = await ThreatBandFor(IntegrationPaths.ShippedSettingsPath(), statistics);
        var lowered = WriteSettings(root => root["threat"]!["gating"]!["killLossCap1"] = 15);

        try
        {
            Assert.Equal("2", shipped);
            Assert.Equal("1", await ThreatBandFor(lowered, statistics));
        }
        finally
        {
            File.Delete(lowered);
        }
    }

    [Fact]
    public async Task RecentSoloShareInTheSettingsFile_DecidesRecentStyleInTheRealEngine()
    {
        var shipped = await RecentStyleFor(IntegrationPaths.ShippedSettingsPath());
        var lowered = WriteSettings(root => root["style"]!["recentSoloSharePercent"] = 40);

        try
        {
            Assert.Equal(StyleClassification.Gang, shipped);
            Assert.Equal(StyleClassification.Solo, await RecentStyleFor(lowered));
        }
        finally
        {
            File.Delete(lowered);
        }
    }

    [Fact]
    public async Task ChainStrengthMaximumInTheSettingsFile_CapsChainRelationshipsFromTheRealEngine()
    {
        var shipped = await ChainStrengthFor(IntegrationPaths.ShippedSettingsPath());
        var lowered = WriteSettings(root => root["groupDetection"]!["chainStrengthMaximum"] = 20);

        try
        {
            Assert.InRange(shipped, 1, 75);
            Assert.InRange(await ChainStrengthFor(lowered), 1, 20);
        }
        finally
        {
            File.Delete(lowered);
        }
    }

    [Fact]
    public async Task ScannedGroup_ShowsStyleThreatAndRelationshipsAndNoPilotIsUnknownWithoutAFailure()
    {
        using var database = new ScanDatabase();
        database.SeedPilot(PilotA, Statistics(shipsDestroyed: 1000, soloKills: 150, shipsLost: 100, soloLosses: 50), "gang");
        database.SeedPilot(PilotB, Statistics(shipsDestroyed: 300, soloKills: 10, shipsLost: 120, soloLosses: 10), "gang");
        database.SeedPilot(PilotC, Statistics(shipsDestroyed: 50, soloKills: 0, shipsLost: 40, soloLosses: 0), "gang");
        database.SeedPilot(PilotD, null, null);
        database.SeedSharedKills(PilotA, PilotB, firstKillmailId: 920000);
        database.SeedSharedKills(PilotB, PilotC, firstKillmailId: 920100);
        database.SeedKill(PilotA, 920200, attackerCount: 1, shipTypeId: CapsuleTypeId);

        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), IntegrationPaths.ShippedSettingsPath());
        var client = database.Client(runtime);
        var ids = new[] { PilotA, PilotB, PilotC, PilotD };

        var pilots = await client.AnalyzePilotsAsync(ids);
        var group = await client.AnalyzeGroupAsync(ids);

        Assert.All(pilots, pilot => Assert.Null(pilot.FailureReason));
        Assert.All(pilots, pilot => Assert.NotEqual(ThreatBandMapper.Unknown, pilot.ThreatBand));
        Assert.Equal(StyleClassification.Gang, pilots[0].RecentStyle);
        Assert.Equal(StyleClassification.Gang, pilots[1].RecentStyle);
        Assert.Equal(StyleClassification.Inactive, pilots[3].RecentStyle);
        Assert.Equal(ThreatBandMapper.None, pilots[3].ThreatBand);
        Assert.NotEqual(ThreatBandMapper.None, pilots[0].ThreatBand);

        Assert.Contains(group.Relationships, r => IsPair(r, PilotA, PilotB) && r.LinkType == RelationshipLinkType.Direct && r.Strength > 0);
        Assert.Contains(group.Relationships, r => IsPair(r, PilotB, PilotC) && r.LinkType == RelationshipLinkType.Direct && r.Strength > 0);
        Assert.Contains(group.Relationships, r => IsPair(r, PilotA, PilotC) && r.LinkType == RelationshipLinkType.Chain && r.Strength <= 75);
        Assert.DoesNotContain(group.Relationships, r => r.PilotACharacterId == PilotD || r.PilotBCharacterId == PilotD);
    }

    private static async Task<string> ThreatBandFor(string settingsPath, zKillStatistics statistics)
    {
        using var database = new ScanDatabase();
        database.SeedPilot(PilotA, statistics, "solo");
        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), settingsPath);

        var result = (await database.Client(runtime).AnalyzePilotsAsync([PilotA])).Single();

        Assert.Null(result.FailureReason);

        return result.ThreatBand;
    }

    private static async Task<StyleClassification> RecentStyleFor(string settingsPath)
    {
        using var database = new ScanDatabase();
        database.SeedPilot(PilotA, Statistics(shipsDestroyed: 100, soloKills: 10, shipsLost: 20, soloLosses: 5), "gang");

        for (var index = 0; index < 5; index++)
            database.SeedKill(PilotA, 930000 + index, attackerCount: index < 2 ? 1 : 3, shipTypeId: 11567);

        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), settingsPath);

        var result = (await database.Client(runtime).AnalyzePilotsAsync([PilotA])).Single();

        Assert.Null(result.FailureReason);

        return result.RecentStyle;
    }

    private static async Task<int> ChainStrengthFor(string settingsPath)
    {
        using var database = new ScanDatabase();
        database.SeedPilot(PilotA, null, null);
        database.SeedPilot(PilotB, null, null);
        database.SeedPilot(PilotC, null, null);
        database.SeedSharedKills(PilotA, PilotB, firstKillmailId: 940000);
        database.SeedSharedKills(PilotB, PilotC, firstKillmailId: 940100);
        using var runtime = new KillrightEngineRuntime(IntegrationPaths.EngineDllPath(), settingsPath);

        var group = await database.Client(runtime).AnalyzeGroupAsync([PilotA, PilotB, PilotC]);
        var chain = group.Relationships.Single(r => IsPair(r, PilotA, PilotC) && r.LinkType == RelationshipLinkType.Chain);

        return chain.Strength;
    }

    private static bool IsPair(PilotRelationship relationship, long first, long second)
    {
        return (relationship.PilotACharacterId == first && relationship.PilotBCharacterId == second)
            || (relationship.PilotACharacterId == second && relationship.PilotBCharacterId == first);
    }

    private static zKillStatistics Statistics(int shipsDestroyed, int soloKills, int shipsLost, int soloLosses, double soloRatio = 15)
    {
        return new zKillStatistics
        {
            shipsDestroyed = shipsDestroyed,
            soloKills = soloKills,
            soloRatio = soloRatio,
            avgGangSize = 4,
            shipsLost = shipsLost,
            soloLosses = soloLosses
        };
    }

    private static JsonNode Section(JsonNode root, string dottedPath)
    {
        return dottedPath.Split('.').Aggregate(root, (node, name) => node[name]!);
    }

    private static string WriteSettings(Action<JsonNode> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(IntegrationPaths.ShippedSettingsPath()))!;
        edit(root);

        var path = Path.Combine(Path.GetTempPath(), $"wiringSettings.{Guid.NewGuid():N}.json");
        File.WriteAllText(path, root.ToJsonString());

        return path;
    }

    private sealed class ScanDatabase : IDisposable
    {
        private readonly KillRightDatabase _database;
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"scanWiring.{Guid.NewGuid():N}.db");
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public ScanDatabase()
        {
            _database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = _path });
            _database.EnsureCreated();
            _database.Open();
            Reader = new EngineInputReader(_database, new SdeReferenceDataStore(_database));
        }

        public IEngineInputReader Reader { get; }

        public RustRecentStyleClient Client(IKillrightEngineRuntime runtime)
        {
            var bands = ApplicationSettingsLoader.Load(IntegrationPaths.ShippedSettingsPath()).Settings!.ThreatBands;

            return new RustRecentStyleClient(runtime, Reader, bands);
        }

        public void SeedPilot(long characterId, zKillStatistics? statistics, string? generalStyle)
        {
            var batch = new ScanWriteBatch();
            batch.AddIdentity(new PilotIdentityCacheRecord
            {
                InputName = $"PILOT {characterId}",
                CharacterId = characterId,
                CharacterName = $"Pilot {characterId}",
                VerifyStatus = VerifyStatus.Partial,
                SecurityStatus = 1.5,
                CorporationId = Corporation(characterId),
                SecurityStatusAtUtc = _now.UtcDateTime,
                CachedAtUtc = _now.UtcDateTime
            });

            if (statistics is not null)
                batch.AddStatistics(new PendingStatistics(characterId, statistics, generalStyle!, false, _now));

            Commit(batch);
        }

        public void SeedKill(long characterId, long killmailId, int attackerCount, long shipTypeId)
        {
            var attackers = new List<KillmailAttacker> { new(characterId, Corporation(characterId), null, 11567) };

            for (var index = 1; index < attackerCount; index++)
                attackers.Add(new KillmailAttacker(990000000 + killmailId * 10 + index, 98000009, null, 11567));

            var batch = new ScanWriteBatch();
            batch.AddKillmails(0, characterId, [Killmail(killmailId, shipTypeId, attackers)]);
            Commit(batch);
        }

        public void SeedSharedKills(long first, long second, long firstKillmailId)
        {
            for (var index = 0; index < 3; index++)
            {
                var attackers = new List<KillmailAttacker>
                {
                    new(first, Corporation(first), null, 11567),
                    new(second, Corporation(second), null, 17738)
                };
                var killmail = Killmail(firstKillmailId + index, 587, attackers);
                var batch = new ScanWriteBatch();
                batch.AddKillmails(0, first, [killmail]);
                batch.AddKillmails(0, second, [killmail]);
                Commit(batch);
            }
        }

        public void Dispose()
        {
            _database.Close();
            File.Delete(_path);
        }

        private static long Corporation(long characterId) => 98000000 + characterId % 1000000;

        private RawKillmail Killmail(long killmailId, long shipTypeId, IReadOnlyList<KillmailAttacker> attackers)
        {
            return new RawKillmail(
                killmailId,
                $"hash{killmailId}",
                _now.AddDays(-1).AddMinutes(-killmailId % 1000),
                30000142,
                null,
                999,
                shipTypeId,
                attackers.Count == 1,
                false,
                attackers);
        }

        private void Commit(ScanWriteBatch batch)
        {
            using var session = _database.OpenScanSession();
            ScanWriter.Commit(session, batch, 11, _now);
        }
    }
}
