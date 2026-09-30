using Killright.Shared.Sde;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.Storage.Sde;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotLastActivitySummaryBuilderTests
{
    [Fact]
    public void Build_LastActivityIsKill_PopulatesKillOnlyFields()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 30000142, 11567, 587, 3, 3074);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, new FakeSdeStore());

        Assert.NotNull(summary);
        Assert.True(summary!.IsKill);
        Assert.Equal("Kill", summary.KillLoss);
        Assert.Equal("Crow", summary.Ship);
        Assert.Equal("Rifter", summary.Victim);
        Assert.Equal("3", summary.Attackers);
        Assert.Equal("Jita", summary.System);
        Assert.Equal("Light Missile Launcher", summary.Weapon);
    }

    [Fact]
    public void Build_LastActivityIdsUnresolved_UsesDashForNames()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 1, 2, 3, 3, 4);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, new FakeSdeStore());

        Assert.Equal("—", summary!.System);
        Assert.Equal("—", summary.Ship);
        Assert.Equal("—", summary.Weapon);
        Assert.Equal("—", summary.Victim);
    }

    [Fact]
    public void Build_LastActivityNullIds_UsesDashForNames()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 30000142, null, null, null, null);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, new FakeSdeStore());

        Assert.Equal("Jita", summary!.System);
        Assert.Equal("—", summary.Ship);
        Assert.Equal("—", summary.Weapon);
        Assert.Equal("—", summary.Victim);
    }

    [Fact]
    public void Build_LastActivityLookupThrows_UsesDashForNames()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 30000142, 11567, 587, 3, 3074);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, new FakeSdeStore(throwOnLookup: true));

        Assert.Equal("—", summary!.System);
        Assert.Equal("—", summary.Ship);
        Assert.Equal("—", summary.Weapon);
        Assert.Equal("—", summary.Victim);
    }

    [Fact]
    public void Build_NoSdeStore_UsesDashForNames()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Kill, 30000142, 11567, 587, 3, 3074);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, null);

        Assert.Equal("—", summary!.System);
        Assert.Equal("—", summary.Ship);
    }

    [Fact]
    public void Build_LastActivityIsLoss_OmitsKillOnlyFields()
    {
        var lastActivity = new PilotRecentKillmail(
            DateTimeOffset.Parse("2026-01-02T03:04:00Z"), zKillActivityType.Loss, 30000142, 587, 11567, 5, 3074);

        var summary = PilotLastActivitySummaryBuilder.Build(lastActivity, new FakeSdeStore());

        Assert.NotNull(summary);
        Assert.False(summary!.IsKill);
        Assert.Equal("Loss", summary.KillLoss);
        Assert.Equal("Rifter", summary.Ship);
        Assert.Equal("Jita", summary.System);
        Assert.Equal("—", summary.Weapon);
        Assert.Equal("—", summary.Victim);
        Assert.Equal("—", summary.Attackers);
    }

    [Fact]
    public void Build_NoLastActivity_LeavesLastActivityNull()
    {
        var summary = PilotLastActivitySummaryBuilder.Build(null, new FakeSdeStore());

        Assert.Null(summary);
    }


    private sealed class FakeSdeStore : ISdeReferenceDataStore
    {
        private readonly bool _throwOnLookup;

        public FakeSdeStore(bool throwOnLookup = false)
        {
            _throwOnLookup = throwOnLookup;
        }

        public string? GetTypeName(long typeId)
        {
            if (_throwOnLookup)
                throw new InvalidOperationException("SDE table missing");

            return typeId switch
            {
                587 => "Rifter",
                11567 => "Crow",
                3074 => "Light Missile Launcher",
                _ => null
            };
        }

        public string? GetSolarSystemName(long systemId)
        {
            if (_throwOnLookup)
                throw new InvalidOperationException("SDE table missing");

            return systemId == 30000142 ? "Jita" : null;
        }

        public bool IsNpcCorporation(long corporationId) => throw new NotSupportedException();

        public IReadOnlySet<long> GetNpcCorporationIds() => throw new NotSupportedException();

        public Task<bool> HasReferenceDataAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SdeMetadata> GetMetadataAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReplaceTablesAsync(SdeReplacementData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordCheckAsync(DateTimeOffset attemptedUtc, string checkResult, bool succeeded, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
