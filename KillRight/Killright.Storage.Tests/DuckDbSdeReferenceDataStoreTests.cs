using DuckDB.NET.Data;
using Killright.Shared.Sde;
using Killright.Storage.Database;
using Killright.Storage.Sde;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbSdeReferenceDataStoreTests
{
    [Fact]
    public async Task GetMetadataAsync_FreshDatabase_ReturnsAllNullFields()
    {
        var (_, store) = CreateStore();

        var metadata = await store.GetMetadataAsync();

        Assert.Null(metadata.BuildNumber);
        Assert.Null(metadata.LastCheckedUtc);
        Assert.Null(metadata.LastUpdatedUtc);
        Assert.Null(metadata.LastCheckResult);
    }

    [Fact]
    public void FreshDatabase_ReferenceTablesExistAndAreEmpty()
    {
        var (_, store) = CreateStore();

        Assert.Null(store.GetTypeName(587));
        Assert.Null(store.GetSolarSystemName(30000142));
        Assert.False(store.IsNpcCorporation(1000001));
        Assert.Empty(store.GetNpcCorporationIds());
    }

    [Fact]
    public async Task ReplaceTablesAsync_ThenGetNpcCorporationIds_ReturnsExactSet()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [],
            SolarSystems: [],
            NpcCorporationIds: [1000001, 1000132],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow));

        var ids = store.GetNpcCorporationIds();

        Assert.Equal(new HashSet<long> { 1000001, 1000132 }, ids);
    }

    [Fact]
    public void NpcCorporationsTableUnreadable_GetNpcCorporationIdsReturnsEmptySetWithoutThrowing()
    {
        var (database, store) = CreateStore();

        using (var connection = new DuckDBConnection(database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE main.sde_npc_corporations;";
            command.ExecuteNonQuery();
        }

        var ids = store.GetNpcCorporationIds();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task ReplaceTablesAsync_ThenLookups_ResolveTypesSystemsAndNpcCorporations()
    {
        var (_, store) = CreateStore();
        var updatedUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter"), new SdeType(11567, "Crow")],
            SolarSystems: [new SdeSolarSystem(30000142, "Jita")],
            NpcCorporationIds: [1000001, 1000132],
            BuildNumber: 3542233,
            UpdatedUtc: updatedUtc));

        Assert.Equal("Rifter", store.GetTypeName(587));
        Assert.Equal("Crow", store.GetTypeName(11567));
        Assert.Null(store.GetTypeName(999999));

        Assert.Equal("Jita", store.GetSolarSystemName(30000142));
        Assert.Null(store.GetSolarSystemName(1));

        Assert.True(store.IsNpcCorporation(1000001));
        Assert.True(store.IsNpcCorporation(1000132));
        Assert.False(store.IsNpcCorporation(98000001));

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(3542233, metadata.BuildNumber);
        Assert.Equal(updatedUtc, metadata.LastCheckedUtc);
        Assert.Equal(updatedUtc, metadata.LastUpdatedUtc);
        Assert.Equal("Replaced", metadata.LastCheckResult);
    }

    [Fact]
    public async Task ReplaceTablesAsync_CalledTwice_SecondReplaceFullyOverwritesFirst()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter")],
            SolarSystems: [new SdeSolarSystem(30000142, "Jita")],
            NpcCorporationIds: [1000001],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow));

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(11567, "Crow")],
            SolarSystems: [new SdeSolarSystem(30000144, "Perimeter")],
            NpcCorporationIds: [1000132],
            BuildNumber: 2,
            UpdatedUtc: DateTimeOffset.UtcNow));

        Assert.Null(store.GetTypeName(587));
        Assert.Equal("Crow", store.GetTypeName(11567));

        Assert.Null(store.GetSolarSystemName(30000142));
        Assert.Equal("Perimeter", store.GetSolarSystemName(30000144));

        Assert.False(store.IsNpcCorporation(1000001));
        Assert.True(store.IsNpcCorporation(1000132));

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(2, metadata.BuildNumber);
    }

    [Fact]
    public async Task RecordCheckAsync_UpdatesCheckedTimeAndResult_WithoutTouchingBuildNumber()
    {
        var (_, store) = CreateStore();
        var replacedUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [],
            SolarSystems: [],
            NpcCorporationIds: [],
            BuildNumber: 3542233,
            UpdatedUtc: replacedUtc));

        var checkedUtc = replacedUtc.AddHours(1);
        await store.RecordCheckAsync(checkedUtc, "UpToDate");

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(3542233, metadata.BuildNumber);
        Assert.Equal(checkedUtc, metadata.LastCheckedUtc);
        Assert.Equal(replacedUtc, metadata.LastUpdatedUtc);
        Assert.Equal("UpToDate", metadata.LastCheckResult);
    }

    private static (KillRightDatabase Database, DuckDbSdeReferenceDataStore Store) CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdeReferenceData.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbSdeReferenceDataStore(database));
    }
}
