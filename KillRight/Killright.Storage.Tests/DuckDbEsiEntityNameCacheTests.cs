using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbEsiEntityNameCacheTests
{
    [Fact]
    public async Task UpsertAsync_CorporationAndAlliance_AreReadBackById()
    {
        var cache = CreateCache();

        await cache.UpsertAsync(
        [
            new EsiEntityName(98765, EsiEntityTypes.Corporation, "Test Corp"),
            new EsiEntityName(99001, EsiEntityTypes.Alliance, "Test Alliance")
        ]);

        var names = await cache.GetNamesAsync([98765, 99001]);

        Assert.Equal("Test Corp", names[98765]);
        Assert.Equal("Test Alliance", names[99001]);
    }

    [Fact]
    public async Task GetNamesAsync_UnknownId_IsOmitted()
    {
        var cache = CreateCache();

        await cache.UpsertAsync([new EsiEntityName(98765, EsiEntityTypes.Corporation, "Test Corp")]);

        var names = await cache.GetNamesAsync([98765, 11111]);

        Assert.Single(names);
        Assert.False(names.ContainsKey(11111));
    }

    [Fact]
    public async Task GetNamesAsync_EmptyRequest_ReturnsEmpty()
    {
        var cache = CreateCache();

        Assert.Empty(await cache.GetNamesAsync([]));
    }

    [Fact]
    public async Task UpsertAsync_ExistingId_ReplacesName()
    {
        var cache = CreateCache();

        await cache.UpsertAsync([new EsiEntityName(98765, EsiEntityTypes.Corporation, "Old Name")]);
        await cache.UpsertAsync([new EsiEntityName(98765, EsiEntityTypes.Corporation, "New Name")]);

        var names = await cache.GetNamesAsync([98765]);

        Assert.Equal("New Name", names[98765]);
    }

    [Fact]
    public async Task UpsertAsync_NameWithApostrophe_IsStoredIntact()
    {
        var cache = CreateCache();

        await cache.UpsertAsync([new EsiEntityName(98765, EsiEntityTypes.Corporation, "T'ral's Corp")]);

        var names = await cache.GetNamesAsync([98765]);

        Assert.Equal("T'ral's Corp", names[98765]);
    }

    private static DuckDbEsiEntityNameCache CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"esiEntityNames.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return new DuckDbEsiEntityNameCache(database);
    }
}
