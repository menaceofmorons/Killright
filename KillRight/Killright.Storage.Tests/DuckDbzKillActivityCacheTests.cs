using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.zKill;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbzKillActivityCacheTests
{
    [Fact]
    public async Task UpsertThenGet_RoundTripsStoredActivity()
    {
        var (_, cache) = CreateCache();

        var activity = new zKillActivity(
            95465499,
            true,
            3,
            1,
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            zKillActivityType.Kill,
            DateTimeOffset.UtcNow);

        await cache.UpsertAsync(activity);

        var cached = await cache.GetAsync(95465499);

        Assert.NotNull(cached);
        Assert.Equal(activity.LastActiveUtc, cached!.LastActiveUtc);
        Assert.Equal(zKillActivityType.Kill, cached.LastActivityType);
    }

    [Fact]
    public async Task GetAsync_RowCheckedLongAgo_IsStillReturned()
    {
        var (_, cache) = CreateCache();

        var activity = new zKillActivity(
            91321792,
            true,
            null,
            null,
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
            zKillActivityType.Loss,
            DateTimeOffset.UtcNow.AddDays(-60));

        await cache.UpsertAsync(activity);

        var cached = await cache.GetAsync(91321792);

        Assert.NotNull(cached);
        Assert.Equal(activity.LastActiveUtc, cached!.LastActiveUtc);
    }

    private static (KillRightDatabase Database, DuckDbzKillActivityCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zkillActivity.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbzKillActivityCache(database));
    }
}
