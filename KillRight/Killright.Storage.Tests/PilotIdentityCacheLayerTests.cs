using Microsoft.Data.Sqlite;
using Killright.Core.Models;
using Killright.Shared;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class PilotIdentityCacheLayerTests
{
    [Fact]
    public async Task UpsertRecordAsync_LayeredRecord_RoundTripsThroughGetRecordAsync()
    {
        var (_, cache) = CreateCache();
        var cachedAt = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var securityAt = new DateTime(2026, 9, 30, 11, 30, 0, DateTimeKind.Utc);

        await cache.UpsertRecordAsync(new PilotIdentityCacheRecord
        {
            InputName = "T'RAL VSENGNE",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = -1.2,
            CorporationId = 98765,
            AllianceId = 99001,
            Birthday = new DateOnly(2015, 6, 12),
            SecurityStatusAtUtc = securityAt,
            CachedAtUtc = cachedAt
        });

        var record = await cache.GetRecordAsync("T'ral Vsengne");

        Assert.NotNull(record);
        Assert.Equal(95465499, record!.CharacterId);
        Assert.Equal(98765, record.CorporationId);
        Assert.Equal(99001, record.AllianceId);
        Assert.Null(record.CorporationName);
        Assert.Equal(new DateOnly(2015, 6, 12), record.Birthday);
        Assert.Equal(securityAt, record.SecurityStatusAtUtc);
        Assert.Equal(cachedAt, record.CachedAtUtc);
    }

    [Fact]
    public async Task UpsertRecordAsync_RecordWithoutCorporation_IsStored()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertRecordAsync(new PilotIdentityCacheRecord
        {
            InputName = "LUKAS NAARII",
            CharacterId = 91321792,
            CharacterName = "Lukas Naarii",
            VerifyStatus = VerifyStatus.Partial,
            CachedAtUtc = DateTime.UtcNow
        });

        var record = await cache.GetRecordAsync("Lukas Naarii");

        Assert.NotNull(record);
        Assert.Null(record!.CorporationId);
        Assert.Null(record.SecurityStatusAtUtc);
    }

    [Fact]
    public async Task UpsertRecordAsync_SameKeyTwice_ReplacesRow()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertRecordAsync(new PilotIdentityCacheRecord { InputName = "LUKAS NAARII", CharacterId = 1, VerifyStatus = VerifyStatus.Partial, CachedAtUtc = DateTime.UtcNow });
        await cache.UpsertRecordAsync(new PilotIdentityCacheRecord { InputName = "LUKAS NAARII", CharacterId = 2, VerifyStatus = VerifyStatus.Partial, CachedAtUtc = DateTime.UtcNow });

        var record = await cache.GetRecordAsync("Lukas Naarii");

        Assert.Equal(2, record!.CharacterId);
    }

    [Fact]
    public async Task GetRecordAsync_NoRow_ReturnsNull()
    {
        var (_, cache) = CreateCache();

        Assert.Null(await cache.GetRecordAsync("syMptom NZ"));
    }

    [Fact]
    public async Task GetRecordAsync_ExpiredRow_IsStillReturned()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertRecordAsync(new PilotIdentityCacheRecord
        {
            InputName = "LUKAS NAARII",
            CharacterId = 91321792,
            VerifyStatus = VerifyStatus.Partial,
            CachedAtUtc = DateTime.UtcNow.AddDays(-400)
        });

        Assert.NotNull(await cache.GetRecordAsync("Lukas Naarii"));
    }

    [Fact]
    public async Task UpsertAsync_PilotWithSecurityStatus_SetsSecurityStatusTimestamp()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertAsync(new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = -1.2,
            Corporation = new Corporation { CorporationId = 98765, Name = "Test Corp" }
        });

        var record = await cache.GetRecordAsync("T'ral Vsengne");

        Assert.NotNull(record!.SecurityStatusAtUtc);
    }

    private static (KillRightDatabase Database, PilotIdentityCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilotIdentityLayers.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new PilotIdentityCache(database));
    }
}
