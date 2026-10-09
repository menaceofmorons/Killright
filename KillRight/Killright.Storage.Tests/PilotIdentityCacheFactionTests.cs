using Killright.Core.Models;
using Killright.Shared;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class PilotIdentityCacheFactionTests
{
    [Fact]
    public async Task UpsertAsync_PilotWithFaction_RoundTripsFactionId()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertAsync(Pilot("T'ral Vsengne", 95465499, 500004));

        var record = await cache.GetRecordAsync("T'ral Vsengne");
        var pilot = await cache.GetAsync("T'ral Vsengne", TimeSpan.FromHours(1));
        var batch = await cache.GetRecordsAsync(["T'ral Vsengne"]);

        Assert.Equal(500004, record!.FactionId);
        Assert.Equal(500004, pilot!.FactionId);
        Assert.Equal(500004, batch["T'RAL VSENGNE"].FactionId);
    }

    [Fact]
    public async Task UpsertAsync_PilotWithoutFaction_RoundTripsNull()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertAsync(Pilot("Lukas Naarii", 91321792, null));

        Assert.Null((await cache.GetRecordAsync("Lukas Naarii"))!.FactionId);
        Assert.Null((await cache.GetAsync("Lukas Naarii", TimeSpan.FromHours(1)))!.FactionId);
    }

    private static Pilot Pilot(string name, long characterId, long? factionId)
    {
        return new Pilot
        {
            InputName = name,
            CharacterId = characterId,
            CharacterName = name,
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = -1.2,
            Corporation = new Corporation { CorporationId = 98765, Name = "Test Corp" },
            FactionId = factionId
        };
    }

    private static (KillRightDatabase Database, PilotIdentityCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilotIdentityFaction.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new PilotIdentityCache(database));
    }
}
