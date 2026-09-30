using Killright.Integration.zKill;
using Killright.Shared.Constants;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;
using Killright.UI.InfoSheet;
using Xunit;

namespace Killright.UI.Tests.InfoSheet;

public sealed class PilotLastActivityResolverTests
{
    private const long Lukas = 2116955190;
    private const long Tral = 2123654623;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_FreshCachedRow_MakesNoRequestAndReturnsTheRow()
    {
        var row = LossRecord(Now.AddDays(-20), checkedAt: Now.AddDays(-6));
        var (resolver, client, cache, _) = Create(cached: row);

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(0, client.Calls);
        Assert.Equal(PilotLastActivitySource.Cache, resolution.Source);
        Assert.Equal(row.Killmail, resolution.Killmail);
        Assert.Equal(0, cache.Upserts);
    }

    [Fact]
    public async Task ResolveAsync_CachedRowAtTheReuseLimit_LooksUpAndUpserts()
    {
        var row = LossRecord(Now.AddDays(-20), checkedAt: Now - CacheDurations.LastKillmailLookup);
        var (resolver, client, cache, _) = Create(cached: row, result: KillResult(Now.AddDays(-3)));

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(1, client.Calls);
        Assert.Equal(PilotLastActivitySource.Live, resolution.Source);
        Assert.Equal(1, cache.Upserts);
        Assert.Equal(Now, cache.Rows[Lukas].CheckedAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredRow_LooksUpOnceAndReplacesTheRow()
    {
        var row = LossRecord(Now.AddDays(-30), checkedAt: Now.AddDays(-8));
        var (resolver, client, cache, _) = Create(cached: row, result: KillResult(Now.AddDays(-2)));

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(1, client.Calls);
        Assert.Equal(Now.AddDays(-2), resolution.Killmail!.KillTimeUtc);
        Assert.Equal(Now.AddDays(-2), cache.Rows[Lukas].Killmail!.KillTimeUtc);
    }

    [Fact]
    public async Task ResolveAsync_LiveKill_MapsPilotShipWeaponVictimAndUniqueAttackerCount()
    {
        var (resolver, _, cache, _) = Create(result: KillResult(Now.AddDays(-9)));

        var resolution = await resolver.ResolveAsync(Lukas);

        var killmail = resolution.Killmail!;
        Assert.Equal(zKillActivityType.Kill, killmail.ActivityType);
        Assert.Equal(623, killmail.ShipTypeId);
        Assert.Equal(3138, killmail.WeaponTypeId);
        Assert.Equal(33151, killmail.VictimShipTypeId);
        Assert.Equal(3, killmail.AttackerCount);
        Assert.Equal(30001586, killmail.SystemId);
        Assert.True(cache.Rows[Lukas].HasKillmail);
        Assert.Equal(138772243, cache.Rows[Lukas].KillmailId);
    }

    [Fact]
    public async Task ResolveAsync_LiveLoss_UsesVictimShipAndLeavesKillOnlyFieldsEmpty()
    {
        var raw = Raw(138775003, Now.AddDays(-9), Lukas, 623, [new KillmailAttacker(110425513, 1, 2, 29984, 24490)]);
        var (resolver, _, _, _) = Create(result: new zKillLastKillmailResult(zKillLastKillmailOutcome.Success, raw, zKillActivityType.Loss));

        var resolution = await resolver.ResolveAsync(Lukas);

        var killmail = resolution.Killmail!;
        Assert.Equal(zKillActivityType.Loss, killmail.ActivityType);
        Assert.Equal(623, killmail.ShipTypeId);
        Assert.Null(killmail.WeaponTypeId);
        Assert.Null(killmail.VictimShipTypeId);
        Assert.Null(killmail.AttackerCount);
    }

    [Fact]
    public async Task ResolveAsync_Failure_WritesNothingAndThrows()
    {
        var (resolver, client, cache, _) = Create(result: new zKillLastKillmailResult(zKillLastKillmailOutcome.Failure, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Lukas));

        Assert.Equal(1, client.Calls);
        Assert.Equal(0, cache.Upserts);
        Assert.Empty(cache.Rows);
    }

    [Theory]
    [InlineData(zKillLastKillmailOutcome.Success)]
    [InlineData(zKillLastKillmailOutcome.NoHistory)]
    public async Task ResolveAsync_NoneFromZKillAndNoLocalKillmail_CachesConfirmedNoneAndReturnsNull(zKillLastKillmailOutcome outcome)
    {
        var (resolver, _, cache, _) = Create(result: new zKillLastKillmailResult(outcome, null, null));

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Null(resolution.Killmail);
        Assert.False(cache.Rows[Lukas].HasKillmail);
        Assert.Equal(Now, cache.Rows[Lukas].CheckedAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_FreshConfirmedNoneRow_MakesNoRequestAndReturnsNull()
    {
        var row = new PilotLastKillmailRecord(Lukas, false, null, null, Now.AddDays(-1));
        var (resolver, client, _, _) = Create(cached: row);

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Null(resolution.Killmail);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_LocalKillmailNewerThanLive_WinsWithoutExtraRequest()
    {
        var local = Local(Now.AddHours(-2));
        var (resolver, client, _, _) = Create(result: KillResult(Now.AddDays(-9)), local: local);

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(local, resolution.Killmail);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_LocalKillmailOlderThanLive_Loses()
    {
        var (resolver, _, _, _) = Create(result: KillResult(Now.AddDays(-1)), local: Local(Now.AddDays(-5)));

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(Now.AddDays(-1), resolution.Killmail!.KillTimeUtc);
    }

    [Fact]
    public async Task ResolveAsync_LocalKillmailNewerThanFreshCachedRow_Wins()
    {
        var row = LossRecord(Now.AddDays(-4), checkedAt: Now.AddDays(-3));
        var local = Local(Now.AddDays(-1));
        var (resolver, client, _, _) = Create(cached: row, local: local);

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(local, resolution.Killmail);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_ConfirmedNoneWithLocalKillmail_ReturnsTheLocalKillmail()
    {
        var local = Local(Now.AddDays(-1));
        var (resolver, _, _, _) = Create(
            result: new zKillLastKillmailResult(zKillLastKillmailOutcome.Success, null, null),
            local: local);

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(local, resolution.Killmail);
    }

    [Fact]
    public async Task ResolveAsync_TwoConcurrentCallsForTheSamePilot_MakeOneLookup()
    {
        var gate = new TaskCompletionSource();
        var (resolver, client, _, _) = Create(result: KillResult(Now.AddDays(-9)), gate: gate.Task);

        var first = resolver.ResolveAsync(Lukas);
        var second = resolver.ResolveAsync(Lukas);
        gate.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, client.Calls);
        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public async Task ResolveAsync_CallsForDifferentPilots_MakeSeparateLookups()
    {
        var gate = new TaskCompletionSource();
        var (resolver, client, _, _) = Create(result: KillResult(Now.AddDays(-9)), gate: gate.Task);

        var first = resolver.ResolveAsync(Lukas);
        var second = resolver.ResolveAsync(Tral);
        gate.SetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_AfterACompletedLookup_TheNextCallStartsANewOne()
    {
        var (resolver, client, cache, _) = Create(result: KillResult(Now.AddDays(-9)));

        await resolver.ResolveAsync(Lukas);
        cache.Rows.Clear();
        await resolver.ResolveAsync(Lukas);

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_AfterAFailedLookup_TheNextCallRetries()
    {
        var (resolver, client, _, _) = Create(result: new zKillLastKillmailResult(zKillLastKillmailOutcome.Failure, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Lukas));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Lukas));

        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ResolveAsync_CacheReadFails_StillReturnsTheLiveResult()
    {
        var (resolver, client, cache, _) = Create(result: KillResult(Now.AddDays(-9)));
        cache.FailReads = true;

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(1, client.Calls);
        Assert.Equal(Now.AddDays(-9), resolution.Killmail!.KillTimeUtc);
    }

    [Fact]
    public async Task ResolveAsync_CacheWriteFails_StillReturnsTheLiveResult()
    {
        var (resolver, _, cache, _) = Create(result: KillResult(Now.AddDays(-9)));
        cache.FailWrites = true;

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(Now.AddDays(-9), resolution.Killmail!.KillTimeUtc);
    }

    [Fact]
    public async Task ResolveAsync_LocalReadFails_StillReturnsTheLiveResult()
    {
        var (resolver, _, _, recent) = Create(result: KillResult(Now.AddDays(-9)));
        recent.Fail = true;

        var resolution = await resolver.ResolveAsync(Lukas);

        Assert.Equal(Now.AddDays(-9), resolution.Killmail!.KillTimeUtc);
    }

    private static (PilotLastActivityResolver Resolver, FakeClient Client, FakeCache Cache, FakeRecent Recent) Create(
        PilotLastKillmailRecord? cached = null,
        zKillLastKillmailResult? result = null,
        PilotRecentKillmail? local = null,
        Task? gate = null)
    {
        var client = new FakeClient(result ?? new zKillLastKillmailResult(zKillLastKillmailOutcome.Success, null, null), gate);
        var cache = new FakeCache();
        var recent = new FakeRecent(local);

        if (cached is not null)
            cache.Rows[cached.CharacterId] = cached;

        return (new PilotLastActivityResolver(client, cache, recent, () => Now), client, cache, recent);
    }

    private static PilotLastKillmailRecord LossRecord(DateTimeOffset killTime, DateTimeOffset checkedAt)
    {
        var killmail = new PilotRecentKillmail(killTime, zKillActivityType.Loss, 30001586, 623, null, null, null);

        return new PilotLastKillmailRecord(Lukas, true, 138775003, killmail, checkedAt);
    }

    private static PilotRecentKillmail Local(DateTimeOffset killTime)
    {
        return new PilotRecentKillmail(killTime, zKillActivityType.Kill, 30004470, 623, 626, 2, 3138);
    }

    private static zKillLastKillmailResult KillResult(DateTimeOffset killTime)
    {
        var attackers = new[]
        {
            new KillmailAttacker(Lukas, 320656615, 99010105, 623, 3138),
            new KillmailAttacker(110425513, 98718813, 99011528, 29984, 24490),
            new KillmailAttacker(110425513, 98718813, 99011528, 29984, 24490),
            new KillmailAttacker(96533891, 98718813, 99011528, 626, 2456),
            new KillmailAttacker(null, null, null, 587, null)
        };

        return new zKillLastKillmailResult(
            zKillLastKillmailOutcome.Success,
            Raw(138772243, killTime, Tral, 33151, attackers),
            zKillActivityType.Kill);
    }

    private static RawKillmail Raw(
        long killmailId,
        DateTimeOffset killTime,
        long victimCharacterId,
        long victimShipTypeId,
        IReadOnlyList<KillmailAttacker> attackers)
    {
        return new RawKillmail(killmailId, "hash", killTime, 30001586, null, victimCharacterId, victimShipTypeId, false, false, attackers);
    }

    private sealed class FakeClient : IzKillClient
    {
        private readonly zKillLastKillmailResult _result;
        private readonly Task? _gate;

        public FakeClient(zKillLastKillmailResult result, Task? gate)
        {
            _result = result;
            _gate = gate;
        }

        public int Calls { get; private set; }

        public long RequestCount => Calls;

        public async Task<zKillLastKillmailResult> GetLastKillmailAsync(long characterId, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (_gate is not null)
                await _gate;

            return _result;
        }

        public Task<zKillRecentKillmailResult> GetRecentKillmailsAsync(long characterId, int pastSeconds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<zKillStatisticsResult> GetStatisticsAsync(long characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeCache : IPilotLastKillmailCache
    {
        public Dictionary<long, PilotLastKillmailRecord> Rows { get; } = [];
        public int Upserts { get; private set; }
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }

        public Task<PilotLastKillmailRecord?> GetAsync(long characterId, CancellationToken cancellationToken = default)
        {
            if (FailReads)
                throw new InvalidOperationException("read failed");

            return Task.FromResult(Rows.GetValueOrDefault(characterId));
        }

        public Task UpsertAsync(PilotLastKillmailRecord record, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
                throw new InvalidOperationException("write failed");

            Upserts++;
            Rows[record.CharacterId] = record;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecent : IRecentKillmailCache
    {
        private readonly PilotRecentKillmail? _killmail;

        public FakeRecent(PilotRecentKillmail? killmail) => _killmail = killmail;

        public bool Fail { get; set; }

        public Task<PilotRecentKillmail?> GetMostRecentKillmailAsync(long characterId, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("local read failed");

            return Task.FromResult(_killmail);
        }

        public Task RemoveExpiredAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
