using Killright.Core.Models;
using Killright.Integration.Esi;
using Killright.Shared;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class PilotIdentityResolverTests
{
    private const string Tral = "T'ral Vsengne";
    private const string Lukas = "Lukas Naarii";
    private const string Symptom = "syMptom NZ";

    [Fact]
    public async Task ResolveAsync_ColdScan_UsesOneBatchPerLayerAndOneCharacterCallPerPilot()
    {
        var fixture = CreateFixture();

        var pilots = await fixture.Resolver.ResolveAsync([Tral, Lukas]);

        Assert.Single(fixture.Esi.NameBatches);
        Assert.Single(fixture.Esi.AffiliationBatches);
        Assert.Equal(2, fixture.Esi.CharacterCalls.Count);
        Assert.Single(fixture.Esi.EntityNameBatches);
        Assert.Equal(VerifyStatus.Partial, pilots[0].VerifyStatus);
        Assert.Equal(95465499, pilots[0].CharacterId);
        Assert.Equal("Test Corp", pilots[0].Corporation!.Name);
        Assert.Equal("Test Alliance", pilots[0].Alliance!.Name);
        Assert.Equal(99001, pilots[0].AllianceId);
        Assert.Equal(-1.2, pilots[0].SecurityStatus);
        Assert.Equal(new DateOnly(2015, 6, 12), pilots[0].Birthday);
        Assert.Equal("Other Corp", pilots[1].Corporation!.Name);
        Assert.Null(pilots[1].Alliance);
        Assert.Null(pilots[1].AllianceId);
    }

    [Fact]
    public async Task ResolveAsync_ResultsFollowInputOrderAndKeepInputNames()
    {
        var fixture = CreateFixture();

        var pilots = await fixture.Resolver.ResolveAsync([Lukas, Tral, "  " + Lukas.ToUpperInvariant()]);

        Assert.Equal([91321792L, 95465499L, 91321792L], pilots.Select(pilot => pilot.CharacterId!.Value).ToArray());
        Assert.Equal(Lukas, pilots[0].InputName);
        Assert.Equal(Lukas.ToUpperInvariant(), pilots[2].InputName);
        Assert.Equal(2, fixture.Esi.CharacterCalls.Count);
    }

    [Fact]
    public async Task ResolveAsync_WarmScanWithinOneHour_RefreshesOnlyAffiliations()
    {
        var fixture = CreateFixture();
        await fixture.Resolver.ResolveAsync([Tral, Lukas]);
        fixture.Esi.Reset();
        fixture.Clock.Advance(TimeSpan.FromMinutes(59));

        var pilots = await fixture.Resolver.ResolveAsync([Tral, Lukas]);

        Assert.Empty(fixture.Esi.NameBatches);
        Assert.Empty(fixture.Esi.CharacterCalls);
        Assert.Empty(fixture.Esi.EntityNameBatches);
        Assert.Single(fixture.Esi.AffiliationBatches);
        Assert.Equal("Test Corp", pilots[0].Corporation!.Name);
        Assert.Equal(-1.2, pilots[0].SecurityStatus);
    }

    [Fact]
    public async Task ResolveAsync_SecurityStatusOlderThanOneHour_RefetchesCharacterButNotName()
    {
        var fixture = CreateFixture();
        await fixture.Resolver.ResolveAsync([Tral]);
        fixture.Esi.Reset();
        fixture.Esi.SecurityStatus = -2.0;
        fixture.Clock.Advance(TimeSpan.FromMinutes(61));

        var pilots = await fixture.Resolver.ResolveAsync([Tral]);

        Assert.Empty(fixture.Esi.NameBatches);
        Assert.Single(fixture.Esi.CharacterCalls);
        Assert.Equal(-2.0, pilots[0].SecurityStatus);
    }

    [Fact]
    public async Task ResolveAsync_CorporationChanged_IsPickedUpOnNextScanWithoutCharacterCall()
    {
        var fixture = CreateFixture();
        await fixture.Resolver.ResolveAsync([Tral]);
        fixture.Esi.Reset();
        fixture.Esi.Affiliations[95465499] = new EsiAffiliation(95465499, 98766, null);

        var pilots = await fixture.Resolver.ResolveAsync([Tral]);

        Assert.Empty(fixture.Esi.CharacterCalls);
        Assert.Equal("Other Corp", pilots[0].Corporation!.Name);
        Assert.Null(pilots[0].Alliance);
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_IsCachedForTwentyFourHoursThenLookedUpAgain()
    {
        var fixture = CreateFixture();

        var first = await fixture.Resolver.ResolveAsync([Symptom]);
        Assert.Equal(VerifyStatus.NoMatch, first[0].VerifyStatus);
        Assert.Single(fixture.Esi.NameBatches);

        fixture.Esi.Reset();
        fixture.Clock.Advance(TimeSpan.FromHours(23));
        var second = await fixture.Resolver.ResolveAsync([Symptom]);
        Assert.Equal(VerifyStatus.NoMatch, second[0].VerifyStatus);
        Assert.Empty(fixture.Esi.NameBatches);

        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.Resolver.ResolveAsync([Symptom]);
        Assert.Single(fixture.Esi.NameBatches);
    }

    [Fact]
    public async Task ResolveAsync_NameLookupFails_PilotFailedAndNothingCached()
    {
        var fixture = CreateFixture();
        fixture.Esi.FailNameLookups = true;

        var pilots = await fixture.Resolver.ResolveAsync([Tral]);

        Assert.Equal(VerifyStatus.Failed, pilots[0].VerifyStatus);
        Assert.Null(await fixture.IdentityCache.GetRecordAsync(Tral));
    }

    [Fact]
    public async Task ResolveAsync_AffiliationFails_PilotShownWithoutCorporationAndIdsNotCached()
    {
        var fixture = CreateFixture();
        fixture.Esi.FailAffiliations = true;
        fixture.Esi.FailCharacters = true;

        var pilots = await fixture.Resolver.ResolveAsync([Tral]);
        var record = await fixture.IdentityCache.GetRecordAsync(Tral);

        Assert.Equal(VerifyStatus.Partial, pilots[0].VerifyStatus);
        Assert.Equal(95465499, pilots[0].CharacterId);
        Assert.Null(pilots[0].Corporation);
        Assert.Null(pilots[0].AllianceId);
        Assert.Equal(95465499, record!.CharacterId);
        Assert.Null(record.CorporationId);
        Assert.Null(record.SecurityStatusAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_AffiliationFailsOnLaterScan_KeepsCachedIdsInStoreButShowsNoCorporation()
    {
        var fixture = CreateFixture();
        await fixture.Resolver.ResolveAsync([Tral]);
        fixture.Esi.Reset();
        fixture.Esi.FailAffiliations = true;

        var pilots = await fixture.Resolver.ResolveAsync([Tral]);
        var record = await fixture.IdentityCache.GetRecordAsync(Tral);

        Assert.Null(pilots[0].Corporation);
        Assert.Equal(98765, record!.CorporationId);
    }

    [Fact]
    public async Task ResolveAsync_EntityNameCallFails_NoNamesCachedAndRetriedNextScan()
    {
        var fixture = CreateFixture();
        fixture.Esi.FailEntityNames = true;

        var first = await fixture.Resolver.ResolveAsync([Tral]);
        Assert.Null(first[0].Corporation);
        Assert.Equal(98765, (await fixture.IdentityCache.GetRecordAsync(Tral))!.CorporationId);

        fixture.Esi.Reset();
        var second = await fixture.Resolver.ResolveAsync([Tral]);

        Assert.Single(fixture.Esi.EntityNameBatches);
        Assert.Equal("Test Corp", second[0].Corporation!.Name);
    }

    [Fact]
    public async Task ResolveAsync_ManyPilotsSameCorporation_CorporationNameRequestedOnce()
    {
        var fixture = CreateFixture();
        fixture.Esi.AddPilot("Pilot One", 1001, 98765, 99001);
        fixture.Esi.AddPilot("Pilot Two", 1002, 98765, 99001);
        fixture.Esi.AddPilot("Pilot Three", 1003, 98765, 99001);

        var pilots = await fixture.Resolver.ResolveAsync(["Pilot One", "Pilot Two", "Pilot Three"]);

        Assert.Single(fixture.Esi.EntityNameBatches);
        Assert.Equal([98765L, 99001L], fixture.Esi.EntityNameBatches[0].OrderBy(id => id).ToArray());
        Assert.All(pilots, pilot => Assert.Equal("Test Corp", pilot.Corporation!.Name));
    }

    [Fact]
    public async Task ResolveAsync_OnePilotCharacterCallFails_OthersUnaffected()
    {
        var fixture = CreateFixture();
        fixture.Esi.FailingCharacterIds.Add(95465499);

        var pilots = await fixture.Resolver.ResolveAsync([Tral, Lukas]);

        Assert.Equal(VerifyStatus.Partial, pilots[0].VerifyStatus);
        Assert.Equal("Test Corp", pilots[0].Corporation!.Name);
        Assert.Null(pilots[0].Birthday);
        Assert.Equal(new DateOnly(2016, 1, 2), pilots[1].Birthday);
        Assert.Equal(0.4, pilots[1].SecurityStatus);
    }

    [Fact]
    public async Task ResolveAsync_CharacterCallsRunConcurrentlyUpToLimit()
    {
        var fixture = CreateFixture(maxConcurrency: 3);
        var names = new List<string>();

        for (var index = 0; index < 9; index++)
        {
            names.Add($"Pilot {index}");
            fixture.Esi.AddPilot($"Pilot {index}", 2000 + index, 98765, null);
        }

        fixture.Esi.CharacterDelay = TimeSpan.FromMilliseconds(30);

        await fixture.Resolver.ResolveAsync(names);

        Assert.InRange(fixture.Esi.PeakConcurrentCharacterCalls, 2, 3);
    }

    [Fact]
    public async Task ResolveAsync_RecordsNamesResolvedCounterWhenTimingOn()
    {
        var fixture = CreateFixture();
        var timings = new Killright.Storage.Diagnostics.ScanTimings();

        await fixture.Resolver.ResolveAsync([Tral, Lukas], timings);

        var counter = Assert.Single(timings.Rows, row => row.Phase == "count_esi_names_resolved");
        Assert.Equal(5, counter.Milliseconds);
    }

    [Fact]
    public async Task ResolveAsync_WithWriteBatch_BuffersIdentityAndNamesInsteadOfWriting()
    {
        var fixture = CreateFixture();
        var writes = new ScanWriteBatch();

        var pilots = await fixture.Resolver.ResolveAsync([Tral, Lukas], writes: writes);

        Assert.Equal(2, writes.Identities.Count);
        Assert.Equal(3, writes.EntityNames.Count);
        Assert.Null(await fixture.IdentityCache.GetRecordAsync(Tral));
        Assert.Equal("Test Corp", pilots[0].Corporation!.Name);
    }

    [Fact]
    public async Task ResolveAsync_BufferedWritesCommittedLater_MatchImmediatePersistence()
    {
        var immediate = CreateFixture();
        var buffered = CreateFixture();
        var writes = new ScanWriteBatch();

        await immediate.Resolver.ResolveAsync([Tral, Lukas]);
        await buffered.Resolver.ResolveAsync([Tral, Lukas], writes: writes);

        using (var session = buffered.Database.OpenScanSession())
            ScanWriter.Commit(session, writes, 11, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        foreach (var name in new[] { Tral, Lukas })
        {
            var expected = await immediate.IdentityCache.GetRecordAsync(name);
            var actual = await buffered.IdentityCache.GetRecordAsync(name);

            Assert.Equal(expected!.CharacterId, actual!.CharacterId);
            Assert.Equal(expected.CorporationId, actual.CorporationId);
            Assert.Equal(expected.AllianceId, actual.AllianceId);
            Assert.Equal(expected.SecurityStatus, actual.SecurityStatus);
            Assert.Equal(expected.Birthday, actual.Birthday);
            Assert.Equal(expected.SecurityStatusAtUtc, actual.SecurityStatusAtUtc);
        }

        var names = await new DuckDbEsiEntityNameCache(buffered.Database).GetNamesAsync([98765, 98766, 99001]);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public async Task ResolveAsync_WithSession_ReadsCachesThroughThatSessionOnly()
    {
        var fixture = CreateFixture();
        await fixture.Resolver.ResolveAsync([Tral, Lukas]);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var before = fixture.Database.ConnectionsOpened;

        using (var session = fixture.Database.OpenScanSession())
        {
            var pilots = await fixture.Resolver.ResolveAsync([Tral, Lukas], session: session, writes: new ScanWriteBatch());

            Assert.Equal("Test Corp", pilots[0].Corporation!.Name);
        }

        Assert.Equal(1, fixture.Database.ConnectionsOpened - before);
    }

    private static Fixture CreateFixture(int maxConcurrency = 8)
    {
        var path = Path.Combine(Path.GetTempPath(), $"identityResolver.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        var clock = new Clock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        var esi = new FakeEsiClient();
        var identityCache = new DuckDbPilotIdentityCache(database);
        var nameCache = new DuckDbEsiEntityNameCache(database);

        return new Fixture(
            new PilotIdentityResolver(esi, identityCache, nameCache, maxConcurrency, () => clock.Now),
            esi,
            identityCache,
            clock,
            database);
    }

    private sealed record Fixture(PilotIdentityResolver Resolver, FakeEsiClient Esi, DuckDbPilotIdentityCache IdentityCache, Clock Clock, KillRightDatabase Database);

    private sealed class Clock
    {
        public Clock(DateTime now)
        {
            Now = now;
        }

        public DateTime Now { get; private set; }

        public void Advance(TimeSpan span) => Now += span;
    }

    private sealed class FakeEsiClient : IEsiClient
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (long Id, string Name)> _characters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, (double SecurityStatus, DateOnly Birthday)> _details = new();
        private int _concurrentCharacterCalls;

        public FakeEsiClient()
        {
            AddPilot(Tral, 95465499, 98765, 99001, -1.2, new DateOnly(2015, 6, 12));
            AddPilot(Lukas, 91321792, 98766, null, 0.4, new DateOnly(2016, 1, 2));
            EntityNames[98765] = "Test Corp";
            EntityNames[98766] = "Other Corp";
            EntityNames[99001] = "Test Alliance";
        }

        public long RequestCount => 0;

        public Dictionary<long, EsiAffiliation> Affiliations { get; } = new();

        public Dictionary<long, string> EntityNames { get; } = new();

        public List<IReadOnlyList<string>> NameBatches { get; } = [];

        public List<IReadOnlyList<long>> AffiliationBatches { get; } = [];

        public List<long> CharacterCalls { get; } = [];

        public List<IReadOnlyList<long>> EntityNameBatches { get; } = [];

        public HashSet<long> FailingCharacterIds { get; } = [];

        public bool FailNameLookups { get; set; }

        public bool FailAffiliations { get; set; }

        public bool FailCharacters { get; set; }

        public bool FailEntityNames { get; set; }

        public double? SecurityStatus { get; set; }

        public TimeSpan CharacterDelay { get; set; }

        public int PeakConcurrentCharacterCalls { get; private set; }

        public void AddPilot(string name, long id, long corporationId, long? allianceId, double securityStatus = 0.1, DateOnly? birthday = null)
        {
            _characters[name] = (id, name);
            _details[id] = (securityStatus, birthday ?? new DateOnly(2018, 1, 1));
            Affiliations[id] = new EsiAffiliation(id, corporationId, allianceId);

            if (!EntityNames.ContainsKey(corporationId))
                EntityNames[corporationId] = "Test Corp";

            if (allianceId is { } alliance && !EntityNames.ContainsKey(alliance))
                EntityNames[alliance] = "Test Alliance";
        }

        public void Reset()
        {
            NameBatches.Clear();
            AffiliationBatches.Clear();
            CharacterCalls.Clear();
            EntityNameBatches.Clear();
            FailNameLookups = false;
            FailAffiliations = false;
            FailCharacters = false;
            FailEntityNames = false;
        }

        public Task<IReadOnlyDictionary<string, EsiNameLookup>> ResolveNamesAsync(IReadOnlyList<string> exactNames, CancellationToken cancellationToken = default)
        {
            NameBatches.Add(exactNames);
            var result = new Dictionary<string, EsiNameLookup>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in exactNames)
            {
                result[name] = FailNameLookups
                    ? EsiNameLookup.Failed
                    : _characters.TryGetValue(name, out var character)
                        ? EsiNameLookup.Matched(character.Id, character.Name)
                        : EsiNameLookup.NoMatch;
            }

            return Task.FromResult<IReadOnlyDictionary<string, EsiNameLookup>>(result);
        }

        public Task<IReadOnlyDictionary<long, EsiAffiliation>> GetAffiliationsAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
        {
            AffiliationBatches.Add(characterIds);

            IReadOnlyDictionary<long, EsiAffiliation> result = FailAffiliations
                ? new Dictionary<long, EsiAffiliation>()
                : characterIds.Where(Affiliations.ContainsKey).ToDictionary(id => id, id => Affiliations[id]);

            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<long, string>> GetEntityNamesAsync(IReadOnlyList<long> entityIds, CancellationToken cancellationToken = default)
        {
            EntityNameBatches.Add(entityIds);

            IReadOnlyDictionary<long, string> result = FailEntityNames
                ? new Dictionary<long, string>()
                : entityIds.Where(EntityNames.ContainsKey).ToDictionary(id => id, id => EntityNames[id]);

            return Task.FromResult(result);
        }

        public async Task<EsiCharacterDetails?> GetCharacterDetailsAsync(long characterId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                CharacterCalls.Add(characterId);
                _concurrentCharacterCalls++;
                PeakConcurrentCharacterCalls = Math.Max(PeakConcurrentCharacterCalls, _concurrentCharacterCalls);
            }

            try
            {
                if (CharacterDelay > TimeSpan.Zero)
                    await Task.Delay(CharacterDelay, cancellationToken);

                if (FailCharacters || FailingCharacterIds.Contains(characterId))
                    return null;

                var details = _details[characterId];
                var affiliation = Affiliations[characterId];
                var name = _characters.Values.First(character => character.Id == characterId).Name;

                return new EsiCharacterDetails(
                    characterId,
                    name,
                    affiliation.CorporationId,
                    affiliation.AllianceId,
                    SecurityStatus ?? details.SecurityStatus,
                    details.Birthday);
            }
            finally
            {
                lock (_gate)
                    _concurrentCharacterCalls--;
            }
        }

        public Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
