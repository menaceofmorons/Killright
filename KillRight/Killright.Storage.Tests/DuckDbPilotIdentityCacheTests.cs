using Killright.Core.Models;
using Killright.Shared;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbPilotIdentityCacheTests
{
    [Fact]
    public async Task UpsertAsync_NoMatch_IsCached()
    {
        var (_, cache) = CreateCache();

        var pilot = new Pilot { InputName = "Lukas Naarii", VerifyStatus = VerifyStatus.NoMatch };
        await cache.UpsertAsync(pilot);

        var cached = await cache.GetAsync("Lukas Naarii", TimeSpan.FromHours(24));

        Assert.NotNull(cached);
        Assert.Equal(VerifyStatus.NoMatch, cached!.VerifyStatus);
    }

    [Fact]
    public async Task UpsertAsync_CompleteLookup_IsCached()
    {
        var (_, cache) = CreateCache();

        var pilot = new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            Corporation = new Corporation { CorporationId = 98765, Name = "Test Corp" },
            Alliance = new Alliance { AllianceId = 99001, Name = "Test Alliance" },
            AllianceId = 99001
        };
        await cache.UpsertAsync(pilot);

        var cached = await cache.GetAsync("T'ral Vsengne", TimeSpan.FromHours(24));

        Assert.NotNull(cached);
    }

    [Fact]
    public async Task UpsertAsync_MissingCorporation_IsNotCached()
    {
        var (_, cache) = CreateCache();

        var pilot = new Pilot
        {
            InputName = "Lukas Naarii",
            CharacterId = 91321792,
            CharacterName = "Lukas Naarii",
            VerifyStatus = VerifyStatus.Partial
        };
        await cache.UpsertAsync(pilot);

        var cached = await cache.GetAsync("Lukas Naarii", TimeSpan.FromHours(24));

        Assert.Null(cached);
    }

    [Fact]
    public async Task UpsertAsync_AllianceIdKnownButAllianceUnresolved_IsNotCached()
    {
        var (_, cache) = CreateCache();

        var pilot = new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            Corporation = new Corporation { CorporationId = 98765, Name = "Test Corp" },
            AllianceId = 99001
        };
        await cache.UpsertAsync(pilot);

        var cached = await cache.GetAsync("T'ral Vsengne", TimeSpan.FromHours(24));

        Assert.Null(cached);
    }

    [Fact]
    public async Task UpsertAsync_Failed_IsNotCached()
    {
        var (_, cache) = CreateCache();

        var pilot = new Pilot { InputName = "syMptom NZ", VerifyStatus = VerifyStatus.Failed };
        await cache.UpsertAsync(pilot);

        var cached = await cache.GetAsync("syMptom NZ", TimeSpan.FromHours(24));

        Assert.Null(cached);
    }

    private static (KillRightDatabase Database, DuckDbPilotIdentityCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilotIdentity.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbPilotIdentityCache(database));
    }
}
