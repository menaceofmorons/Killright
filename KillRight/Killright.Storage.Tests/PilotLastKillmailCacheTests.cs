using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Killright.Storage.zKill;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class PilotLastKillmailCacheTests
{
    private const long Lukas = 2116955190;
    private static readonly DateTimeOffset Checked = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_NoRow_ReturnsNull()
    {
        var cache = CreateCache();

        Assert.Null(await cache.GetAsync(Lukas));
    }

    [Fact]
    public async Task UpsertThenGet_KillRoundTripsEveryField()
    {
        var cache = CreateCache();
        var killTime = new DateTimeOffset(2026, 9, 28, 18, 39, 3, TimeSpan.Zero);
        var killmail = new PilotRecentKillmail(killTime, zKillActivityType.Kill, 30001586, 623, 33151, 2, 3138);

        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, true, 138772243, killmail, Checked));

        var stored = await cache.GetAsync(Lukas);

        Assert.NotNull(stored);
        Assert.True(stored!.HasKillmail);
        Assert.Equal(138772243, stored.KillmailId);
        Assert.Equal(killmail, stored.Killmail);
        Assert.Equal(Checked, stored.CheckedAtUtc);
    }

    [Fact]
    public async Task UpsertThenGet_LossRoundTripsWithNullKillOnlyFields()
    {
        var cache = CreateCache();
        var killmail = new PilotRecentKillmail(Checked.AddDays(-2), zKillActivityType.Loss, 30001586, 623, null, null, null);

        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, true, 138775003, killmail, Checked));

        var stored = await cache.GetAsync(Lukas);

        Assert.Equal(killmail, stored!.Killmail);
        Assert.Equal(zKillActivityType.Loss, stored.Killmail!.ActivityType);
    }

    [Fact]
    public async Task UpsertThenGet_ConfirmedNoneRoundTrips()
    {
        var cache = CreateCache();

        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, false, null, null, Checked));

        var stored = await cache.GetAsync(Lukas);

        Assert.NotNull(stored);
        Assert.False(stored!.HasKillmail);
        Assert.Null(stored.Killmail);
        Assert.Null(stored.KillmailId);
        Assert.Equal(Checked, stored.CheckedAtUtc);
    }

    [Fact]
    public async Task Upsert_ReplacesTheExistingRow()
    {
        var cache = CreateCache();
        var killmail = new PilotRecentKillmail(Checked.AddDays(-9), zKillActivityType.Kill, 30001586, 623, 626, 2, 3138);

        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, true, 1, killmail, Checked.AddDays(-8)));
        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, false, null, null, Checked));

        var stored = await cache.GetAsync(Lukas);

        Assert.False(stored!.HasKillmail);
        Assert.Equal(Checked, stored.CheckedAtUtc);
    }

    [Fact]
    public async Task Rows_AreKeptPerCharacter()
    {
        var cache = CreateCache();
        var killmail = new PilotRecentKillmail(Checked, zKillActivityType.Kill, 1, 2, 3, 4, 5);

        await cache.UpsertAsync(new PilotLastKillmailRecord(Lukas, true, 10, killmail, Checked));
        await cache.UpsertAsync(new PilotLastKillmailRecord(91321792, false, null, null, Checked));

        Assert.True((await cache.GetAsync(Lukas))!.HasKillmail);
        Assert.False((await cache.GetAsync(91321792))!.HasKillmail);
    }

    private static PilotLastKillmailCache CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilotLastKillmail.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return new PilotLastKillmailCache(database);
    }
}
