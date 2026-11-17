using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class EsiEntityNameCacheTests
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
    public async Task UpsertAsync_MoreThanOneChunkOfHostileNames_RoundTripsEveryName()
    {
        var cache = CreateCache();
        var entities = Enumerable.Range(1, 1200)
            .Select(id => new EsiEntityName(id, EsiEntityTypes.Corporation, $"O'Corp {id}'); DROP TABLE esi_entity_name_cache; --"))
            .ToList();

        await cache.UpsertAsync(entities);
        await cache.UpsertAsync(entities);

        var names = await cache.GetNamesAsync(entities.Select(entity => entity.EntityId).ToList());

        Assert.Equal(1200, names.Count);
        Assert.Equal(entities[1199].Name, names[1200]);
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

    private static EsiEntityNameCache CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"esiEntityNames.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return new EsiEntityNameCache(database);
    }
}
