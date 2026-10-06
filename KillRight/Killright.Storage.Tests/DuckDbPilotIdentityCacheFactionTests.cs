using DuckDB.NET.Data;
using Killright.Core.Models;
using Killright.Shared;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbPilotIdentityCacheFactionTests
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

    [Fact]
    public void EnsureCreated_DatabaseWithoutFactionColumn_AddsColumnAndNullsEverySecurityTimestamp()
    {
        var (database, cache) = CreateCache();

        Execute(database, "ALTER TABLE main.pilot_identity_cache DROP COLUMN faction_id;");
        Execute(database, """
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, cached_at_utc, security_status_at_utc)
            VALUES ('T''RAL VSENGNE', 1, 'T''ral Vsengne', 'Partial', -1.2, '2026-10-06T10:00:00Z', '2026-10-06T10:00:00Z'),
                   ('LUKAS NAARII', 2, 'Lukas Naarii', 'Partial', 0.4, '2026-10-06T10:00:00Z', '2026-10-06T10:00:00Z');
            """);

        database.EnsureCreated();

        Assert.True(FactionColumnExists(database));
        Assert.Equal(2, CountWhere(database, "security_status_at_utc IS NULL"));
        Assert.Null(cache.GetRecordAsync("T'ral Vsengne").GetAwaiter().GetResult()!.FactionId);
    }

    [Fact]
    public void EnsureCreated_SecondStartup_LeavesTimestampsUntouched()
    {
        var (database, _) = CreateCache();

        Execute(database, "ALTER TABLE main.pilot_identity_cache DROP COLUMN faction_id;");
        Execute(database, """
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, cached_at_utc, security_status_at_utc)
            VALUES ('T''RAL VSENGNE', 1, 'T''ral Vsengne', 'Partial', -1.2, '2026-10-06T10:00:00Z', '2026-10-06T10:00:00Z');
            """);

        database.EnsureCreated();
        Execute(database, "UPDATE main.pilot_identity_cache SET security_status_at_utc = '2026-10-06T11:30:00Z';");
        var before = SecurityTimestampText(database);

        database.EnsureCreated();

        Assert.NotNull(before);
        Assert.Equal(before, SecurityTimestampText(database));
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

    private static bool FactionColumnExists(KillRightDatabase database)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'pilot_identity_cache' AND column_name = 'faction_id';";

        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static string? SecurityTimestampText(KillRightDatabase database)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(security_status_at_utc AS VARCHAR) FROM main.pilot_identity_cache LIMIT 1;";

        return command.ExecuteScalar() as string;
    }

    private static long CountWhere(KillRightDatabase database, string predicate)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.pilot_identity_cache WHERE {predicate};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static (KillRightDatabase Database, DuckDbPilotIdentityCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pilotIdentityFaction.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbPilotIdentityCache(database));
    }
}
