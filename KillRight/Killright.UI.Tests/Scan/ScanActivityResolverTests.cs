using Killright.Core.Activity;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanActivityResolverTests
{
    private const long Lukas = 95465499;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_KillmailDerivedNewerThanStored_MovesLastActiveForward()
    {
        var stored = Stored(lastActive: Now.AddDays(-5), type: zKillActivityType.Kill);
        var derived = Derived(lastActive: Now.AddDays(-1), type: zKillActivityType.Loss);

        var resolution = ScanActivityResolver.Resolve(Lukas, null, stored, derived, null, null, Now);

        Assert.True(resolution.Persist);
        Assert.Equal(Now.AddDays(-1), resolution.Activity!.LastActiveUtc);
        Assert.Equal(zKillActivityType.Loss, resolution.Activity.LastActivityType);
    }

    [Fact]
    public void Resolve_KillmailDerivedOlderThanStored_KeepsTheStoredLastActive()
    {
        var stored = Stored(lastActive: Now.AddDays(-1), type: zKillActivityType.Kill);
        var derived = Derived(lastActive: Now.AddDays(-5), type: zKillActivityType.Loss);

        var resolution = ScanActivityResolver.Resolve(Lukas, null, stored, derived, null, null, Now);

        Assert.Equal(Now.AddDays(-1), resolution.Activity!.LastActiveUtc);
        Assert.Equal(zKillActivityType.Kill, resolution.Activity.LastActivityType);
    }

    [Fact]
    public void Resolve_StatisticsDerivedLaterThanStored_AdvancesLastActive()
    {
        var stored = Stored(lastActive: Now.AddDays(-40), type: zKillActivityType.Kill);
        var statistics = new zKillStatistics
        {
            months = new Dictionary<string, zKillStatisticsMonth>
            {
                ["202609"] = new() { Year = 2026, Month = 9, ShipsDestroyed = 3, ShipsLost = 1 }
            }
        };
        var (expected, _) = LastActiveDeriver.Derive(statistics.months, Now);

        var resolution = ScanActivityResolver.Resolve(Lukas, statistics, stored, null, null, null, Now);

        Assert.Equal(expected, resolution.Activity!.LastActiveUtc);
    }

    [Fact]
    public void Resolve_DerivedCounts_ReplaceTheStoredCounts()
    {
        var stored = Stored(lastActive: Now.AddDays(-5), type: zKillActivityType.Kill, killsWeek: 9, soloWeek: 8);
        var derived = Derived(lastActive: Now.AddDays(-1), type: zKillActivityType.Kill, killsWeek: 2, soloWeek: 1);

        var resolution = ScanActivityResolver.Resolve(Lukas, null, stored, derived, null, null, Now);

        Assert.Equal(2, resolution.Activity!.KillsWeek);
        Assert.Equal(1, resolution.Activity.SoloWeek);
    }

    [Fact]
    public void Resolve_WithoutDerived_KeepsTheStoredCounts()
    {
        var stored = Stored(lastActive: Now.AddDays(-5), type: zKillActivityType.Kill, killsWeek: 9, soloWeek: 8);

        var resolution = ScanActivityResolver.Resolve(Lukas, null, stored, null, null, null, Now);

        Assert.Equal(9, resolution.Activity!.KillsWeek);
        Assert.Equal(8, resolution.Activity.SoloWeek);
    }

    [Fact]
    public void Resolve_NothingStoredNothingDerivedNoCall_IsNotPersistedAndReturnsTheDerivedValue()
    {
        var resolution = ScanActivityResolver.Resolve(Lukas, null, null, null, null, null, Now);

        Assert.False(resolution.Persist);
        Assert.Null(resolution.Activity);
    }

    [Fact]
    public void Resolve_NothingStoredButARecentCallSucceeded_IsPersistedWithTheCallTime()
    {
        var resolution = ScanActivityResolver.Resolve(Lukas, null, null, null, Now, 3600, Now);

        Assert.True(resolution.Persist);
        Assert.Equal(Now, resolution.Activity!.LastSuccessfulRecentCallUtc);
        Assert.False(resolution.Activity.HasPublicActivityData);
        Assert.Equal(
            RecentCallScheduler.ResolveCoverageStartUtc(null, null, 3600, Now),
            resolution.Activity.RecentCoverageStartUtc);
    }

    [Fact]
    public void Resolve_NoNewCall_KeepsTheStoredCallAndCoverage()
    {
        var stored = Stored(lastActive: Now.AddDays(-2), type: zKillActivityType.Kill) with
        {
            LastSuccessfulRecentCallUtc = Now.AddMinutes(-30),
            RecentCoverageStartUtc = Now.AddDays(-14)
        };

        var resolution = ScanActivityResolver.Resolve(Lukas, null, stored, null, null, null, Now);

        Assert.Equal(Now.AddMinutes(-30), resolution.Activity!.LastSuccessfulRecentCallUtc);
        Assert.Equal(
            RecentCallScheduler.ResolveCoverageStartUtc(stored.RecentCoverageStartUtc, stored.LastSuccessfulRecentCallUtc, null, Now),
            resolution.Activity.RecentCoverageStartUtc);
    }

    [Fact]
    public void Resolve_DerivedHasPublicActivityOnly_MarksTheMergedRecordAsHavingData()
    {
        var derived = new zKillActivity(Lukas, true, 0, 0, null, null, Now);

        var resolution = ScanActivityResolver.Resolve(Lukas, null, null, derived, null, null, Now);

        Assert.True(resolution.Persist);
        Assert.True(resolution.Activity!.HasPublicActivityData);
        Assert.Equal(0, resolution.Activity.KillsWeek);
    }

    [Fact]
    public void Resolve_LastKillFromDerived_IsCarriedToTheMergedRecord()
    {
        var derived = Derived(lastActive: Now.AddDays(-1), type: zKillActivityType.Loss) with { LastKillUtc = Now.AddDays(-2) };

        var resolution = ScanActivityResolver.Resolve(Lukas, null, null, derived, null, null, Now);

        Assert.Equal(Now.AddDays(-2), resolution.Activity!.LastKillUtc);
    }

    private static zKillActivity Stored(
        DateTimeOffset lastActive,
        zKillActivityType type,
        int? killsWeek = 1,
        int? soloWeek = 0)
    {
        return new zKillActivity(Lukas, true, killsWeek, soloWeek, lastActive, type, Now.AddHours(-2));
    }

    private static zKillActivity Derived(
        DateTimeOffset lastActive,
        zKillActivityType type,
        int? killsWeek = 1,
        int? soloWeek = 0)
    {
        return new zKillActivity(Lukas, true, killsWeek, soloWeek, lastActive, type, Now);
    }
}
