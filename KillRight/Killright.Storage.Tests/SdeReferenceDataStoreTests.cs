using Microsoft.Data.Sqlite;
using Killright.Shared.Sde;
using Killright.Storage.Database;
using Killright.Storage.Sde;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class SdeReferenceDataStoreTests
{
    [Fact]
    public async Task GetMetadataAsync_FreshDatabase_ReturnsAllNullFields()
    {
        var (_, store) = CreateStore();

        var metadata = await store.GetMetadataAsync();

        Assert.Null(metadata.BuildNumber);
        Assert.Null(metadata.LastCheckedUtc);
        Assert.Null(metadata.LastUpdatedUtc);
        Assert.Null(metadata.LastAttemptUtc);
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
    public async Task HasReferenceDataAsync_FreshDatabase_ReturnsFalse()
    {
        var (_, store) = CreateStore();

        Assert.False(await store.HasReferenceDataAsync());
    }

    [Fact]
    public async Task HasReferenceDataAsync_AfterReplaceTablesAsync_ReturnsTrue()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [],
            SolarSystems: [],
            NpcCorporationIds: [1000001],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow,
            Factions: [new SdeFaction(500001, "Caldari State")]));

        Assert.True(await store.HasReferenceDataAsync());
    }

    [Fact]
    public async Task HasReferenceDataAsync_OtherTablesPopulatedButFactionsEmpty_ReturnsFalse()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(
            types: [new SdeType(587, "Rifter")],
            systems: [new SdeSolarSystem(30000142, "Jita")],
            npcCorporationIds: [1000001]));

        Assert.False(await store.HasReferenceDataAsync());
    }

    [Fact]
    public async Task ReplaceTablesAsync_Factions_LoadsTableAndNameLookupReturnsEnglishName()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [],
            SolarSystems: [],
            NpcCorporationIds: [],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow,
            Factions: [new SdeFaction(500001, "Caldari State"), new SdeFaction(500011, "Angel Cartel")]));

        Assert.Equal("Caldari State", store.GetFactionName(500001));
        Assert.Equal("Angel Cartel", store.GetFactionName(500011));
        Assert.Null(store.GetFactionName(500099));
        Assert.Equal(2, CountRows(database, "main.sde_factions"));
    }

    [Fact]
    public async Task ReplaceTablesAsync_FactionsReplaced_ClearsFactionNameCache()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(factions: [new SdeFaction(500001, "Caldari State")]));
        Assert.Equal("Caldari State", store.GetFactionName(500001));

        await store.ReplaceTablesAsync(SdeData(factions: [new SdeFaction(500001, "Caldari Renamed")]));

        Assert.Equal("Caldari Renamed", store.GetFactionName(500001));
    }

    [Fact]
    public void GetFactionName_FreshDatabase_ReturnsNull()
    {
        var (_, store) = CreateStore();

        Assert.Null(store.GetFactionName(500001));
    }

    [Fact]
    public void GetFactionName_FactionsTableUnreadable_ReturnsNullWithoutThrowing()
    {
        var (database, store) = CreateStore();

        ExecuteNonQuery(database, "DROP TABLE main.sde_factions;");

        Assert.Null(store.GetFactionName(500001));
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

        using (var connection = database.OpenConnection())
        {
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
        Assert.Equal(updatedUtc, metadata.LastAttemptUtc);
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
    public async Task ReplaceTablesAsync_FailureMidWay_LeavesThePreviousTablesIntact()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter")],
            SolarSystems: [new SdeSolarSystem(30000142, "Jita")],
            NpcCorporationIds: [1000001],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow,
            Factions: [new SdeFaction(500004, "Gallente Federation")]));

        await Assert.ThrowsAnyAsync<Exception>(() => store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(11567, "Crow")],
            SolarSystems: [new SdeSolarSystem(30000144, "Perimeter"), new SdeSolarSystem(30000144, "Perimeter Again")],
            NpcCorporationIds: [1000132],
            BuildNumber: 2,
            UpdatedUtc: DateTimeOffset.UtcNow)));

        Assert.Equal("Rifter", store.GetTypeName(587));
        Assert.Null(store.GetTypeName(11567));
        Assert.Equal("Jita", store.GetSolarSystemName(30000142));
        Assert.True(store.IsNpcCorporation(1000001));
        Assert.Equal("Gallente Federation", store.GetFactionName(500004));
        Assert.Equal(1, (await store.GetMetadataAsync()).BuildNumber);
        Assert.Equal(0, CountRows(database, "sqlite_schema WHERE name LIKE '%_staging'"));
    }

    [Fact]
    public async Task ReplaceTablesAsync_ReplacedTables_StayStrict()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(types: [new SdeType(587, "Rifter")], factions: [new SdeFaction(500004, "Gallente Federation")]));

        Assert.Equal(4, CountRows(database, "pragma_table_list WHERE schema = 'main' AND strict = 1 AND name IN ('sde_types', 'sde_solar_systems', 'sde_npc_corporations', 'sde_factions')"));
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
        await store.RecordCheckAsync(checkedUtc, "UpToDate", succeeded: true);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(3542233, metadata.BuildNumber);
        Assert.Equal(checkedUtc, metadata.LastCheckedUtc);
        Assert.Equal(replacedUtc, metadata.LastUpdatedUtc);
        Assert.Equal(checkedUtc, metadata.LastAttemptUtc);
        Assert.Equal("UpToDate", metadata.LastCheckResult);
    }

    [Fact]
    public async Task RecordCheckAsync_Failed_UpdatesAttemptTimeOnly_LeavesCheckedTimeUntouched()
    {
        var (_, store) = CreateStore();
        var attemptedUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await store.RecordCheckAsync(attemptedUtc, "ManifestFailure", succeeded: false);

        var metadata = await store.GetMetadataAsync();
        Assert.Null(metadata.LastCheckedUtc);
        Assert.Equal(attemptedUtc, metadata.LastAttemptUtc);
        Assert.Equal("ManifestFailure", metadata.LastCheckResult);
    }

    [Fact]
    public async Task GetTypeName_RepeatedLookup_ServedFromCacheAfterTableChange()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(types: [new SdeType(587, "Rifter")]));
        Assert.Equal("Rifter", store.GetTypeName(587));

        ExecuteNonQuery(database, "UPDATE main.sde_types SET name = 'Changed' WHERE type_id = 587;");

        Assert.Equal("Rifter", store.GetTypeName(587));
    }

    [Fact]
    public async Task GetSolarSystemName_RepeatedLookup_ServedFromCacheAfterTableChange()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(systems: [new SdeSolarSystem(30000142, "Jita")]));
        Assert.Equal("Jita", store.GetSolarSystemName(30000142));

        ExecuteNonQuery(database, "UPDATE main.sde_solar_systems SET name = 'Changed' WHERE system_id = 30000142;");

        Assert.Equal("Jita", store.GetSolarSystemName(30000142));
    }

    [Fact]
    public async Task GetNpcCorporationIds_RepeatedCall_ServedFromCacheAfterTableChange()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(npcCorporationIds: [1000001]));
        Assert.Equal(new HashSet<long> { 1000001 }, store.GetNpcCorporationIds());

        ExecuteNonQuery(database, "INSERT INTO main.sde_npc_corporations (corporation_id) VALUES (1000132);");

        Assert.Equal(new HashSet<long> { 1000001 }, store.GetNpcCorporationIds());
    }

    [Fact]
    public async Task ReplaceTablesAsync_ClearsAllCaches()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(
            types: [new SdeType(587, "Rifter")],
            systems: [new SdeSolarSystem(30000142, "Jita")],
            npcCorporationIds: [1000001]));

        Assert.Equal("Rifter", store.GetTypeName(587));
        Assert.Equal("Jita", store.GetSolarSystemName(30000142));
        Assert.Single(store.GetNpcCorporationIds());

        await store.ReplaceTablesAsync(SdeData(
            types: [new SdeType(587, "Rifter Renamed")],
            systems: [new SdeSolarSystem(30000142, "Jita Renamed")],
            npcCorporationIds: [1000001, 1000132]));

        Assert.Equal("Rifter Renamed", store.GetTypeName(587));
        Assert.Equal("Jita Renamed", store.GetSolarSystemName(30000142));
        Assert.Equal(2, store.GetNpcCorporationIds().Count);
    }

    [Fact]
    public async Task GetTypeName_MissThenRowInserted_LookedUpAgain()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData());
        Assert.Null(store.GetTypeName(587));

        ExecuteNonQuery(database, "INSERT INTO main.sde_types (type_id, name) VALUES (587, 'Rifter');");

        Assert.Equal("Rifter", store.GetTypeName(587));
    }

    [Fact]
    public async Task GetSolarSystemName_MissThenRowInserted_LookedUpAgain()
    {
        var (database, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData());
        Assert.Null(store.GetSolarSystemName(30000142));

        ExecuteNonQuery(database, "INSERT INTO main.sde_solar_systems (system_id, name) VALUES (30000142, 'Jita');");

        Assert.Equal("Jita", store.GetSolarSystemName(30000142));
    }

    [Fact]
    public void GetNpcCorporationIds_EmptyResult_NotCachedPermanently()
    {
        var (database, store) = CreateStore();

        Assert.Empty(store.GetNpcCorporationIds());

        ExecuteNonQuery(database, "INSERT INTO main.sde_npc_corporations (corporation_id) VALUES (1000001);");

        Assert.Equal(new HashSet<long> { 1000001 }, store.GetNpcCorporationIds());
    }

    [Fact]
    public async Task ConcurrentLookups_ReturnConsistentResults()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(SdeData(
            types: [new SdeType(587, "Rifter"), new SdeType(11567, "Crow")],
            systems: [new SdeSolarSystem(30000142, "Jita")],
            npcCorporationIds: [1000001, 1000132]));

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => (
                store.GetTypeName(587),
                store.GetTypeName(11567),
                store.GetSolarSystemName(30000142),
                store.GetNpcCorporationIds().Count)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(("Rifter", "Crow", "Jita", 2), result));
    }

    private static SdeReplacementData SdeData(
        IReadOnlyList<SdeType>? types = null,
        IReadOnlyList<SdeSolarSystem>? systems = null,
        IReadOnlyList<long>? npcCorporationIds = null,
        IReadOnlyList<SdeFaction>? factions = null)
    {
        return new SdeReplacementData(
            Types: types ?? [],
            SolarSystems: systems ?? [],
            NpcCorporationIds: npcCorporationIds ?? [],
            BuildNumber: 1,
            UpdatedUtc: DateTimeOffset.UtcNow,
            Factions: factions);
    }

    private static long CountRows(KillRightDatabase database, string table)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void ExecuteNonQuery(KillRightDatabase database, string sql)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static (KillRightDatabase Database, SdeReferenceDataStore Store) CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdeReferenceData.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new SdeReferenceDataStore(database));
    }
}
